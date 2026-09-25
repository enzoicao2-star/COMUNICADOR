param(
    [string]$RootPath = (Join-Path $env:LOCALAPPDATA 'Comunicador\Carousel'),
    [switch]$Once,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $RootPath | Out-Null
$lockPath = Join-Path $RootPath 'worker.lock'
$workerLock = $null
for ($attempt = 0; $attempt -lt 50 -and $null -eq $workerLock; $attempt++) {
    try {
        $workerLock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
    } catch [System.IO.IOException] {
        Start-Sleep -Milliseconds 200 # Aguarda o worker anterior terminar após "Desligar".
    }
}
if ($null -eq $workerLock) { exit 0 }
if (-not $DryRun) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()
}
$script:carouselForm = $null
$script:carouselBitmap = $null
$script:visibleUntil = [DateTime]::MinValue

function Read-CarouselJson([string]$Path) {
    if (-not [System.IO.File]::Exists($Path)) { return $null }
    return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Save-CarouselJson([string]$Path, [object]$Value) {
    $temporary = "$Path.tmp"
    [System.IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Copy($temporary, $Path, $true)
    [System.IO.File]::Delete($temporary)
}

function Close-CenterImage {
    if ($null -ne $script:carouselForm) {
        if (-not $script:carouselForm.IsDisposed) { $script:carouselForm.Close() }
        $script:carouselForm.Dispose()
        $script:carouselForm = $null
    }
    if ($null -ne $script:carouselBitmap) {
        $script:carouselBitmap.Dispose()
        $script:carouselBitmap = $null
    }
}

function Show-CenterImage([string]$Path, [int]$Seconds) {
    Close-CenterImage
    $bitmap = [System.Drawing.Image]::FromFile($Path)
    try {
        $area = [System.Windows.Forms.Screen]::FromPoint([System.Windows.Forms.Cursor]::Position).WorkingArea
        $width = [Math]::Min([Math]::Max($bitmap.Width + 24, 240), [int]($area.Width * 0.8))
        $height = [Math]::Min([Math]::Max($bitmap.Height + 24, 180), [int]($area.Height * 0.8))
        $form = New-Object System.Windows.Forms.Form
        $form.Text = 'Imagem do Comunicador'
        $form.FormBorderStyle = 'FixedDialog'
        $form.MaximizeBox = $false
        $form.TopMost = $true
        $form.BackColor = [System.Drawing.Color]::FromArgb(24, 28, 36)
        $form.ClientSize = [System.Drawing.Size]::new($width, $height)
        $form.StartPosition = 'Manual'
        $form.Location = [System.Drawing.Point]::new(
            $area.Left + [int](($area.Width - $width) / 2),
            $area.Top + [int](($area.Height - $height) / 2))
        $picture = New-Object System.Windows.Forms.PictureBox
        $picture.Dock = 'Fill'
        $picture.SizeMode = 'Zoom'
        $picture.Image = $bitmap
        $form.Controls.Add($picture)
        $form.Show()
        $form.BringToFront()
        $script:carouselForm = $form
        $script:carouselBitmap = $bitmap
        $script:visibleUntil = [DateTime]::UtcNow.AddSeconds($Seconds)
    } catch {
        $bitmap.Dispose()
        throw
    }
}

function Apply-CarouselImage([string]$Path, [string]$Target, [int]$Seconds) {
    if ($DryRun) {
        Add-Content -LiteralPath (Join-Path $RootPath 'applied.log') -Value "$Target|$Path"
        return
    }
    Show-CenterImage $Path $Seconds
}

$activePath = Join-Path $RootPath 'active.json'
$statePath = Join-Path $RootPath 'state.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
try {
    while ($true) {
        try {
            $active = Read-CarouselJson $activePath
            if ($null -eq $active -or -not $active.enabled) { break }
            if ($active.target -ne 'center_image') {
                # Desativa carrosséis antigos; nenhum worker novo troca o fundo do Windows.
                $active.enabled = $false
                Save-CarouselJson $activePath $active
                if (-not $DryRun) { Remove-ItemProperty -Path $runKey -Name 'ComunicadorCarousel' -ErrorAction SilentlyContinue }
                break
            }
            $state = Read-CarouselJson $statePath
            if ($null -eq $state -or $state.session_id -ne $active.session_id) {
                $state = [pscustomobject]@{ session_id = $active.session_id; next_index = 0; next_at_utc = '' }
            }
            $now = [DateTime]::UtcNow
            $due = $state.next_at_utc -eq '' -or $now -ge [DateTime]::Parse($state.next_at_utc).ToUniversalTime()
            if ($due) {
                $index = [int]$state.next_index
                if ($index -ge @($active.images).Count) {
                    $active.enabled = $false
                    Save-CarouselJson $activePath $active
                    if (-not $DryRun) { Remove-ItemProperty -Path $runKey -Name 'ComunicadorCarousel' -ErrorAction SilentlyContinue }
                    break
                }
                $fileName = [string]$active.images[$index]
                if ([System.IO.Path]::GetFileName($fileName) -ne $fileName) { throw 'Nome de imagem inválido.' }
                $imagePath = Join-Path (Join-Path $RootPath ([string]$active.folder)) $fileName
                if (-not [System.IO.File]::Exists($imagePath)) { throw "Imagem ausente: $fileName" }
                Apply-CarouselImage $imagePath ([string]$active.target) ([int]$active.duration_seconds)
                $index++
                if ($index -ge @($active.images).Count) {
                    if (-not $active.repeat) {
                        $state.next_index = $index
                        $state.next_at_utc = $now.AddSeconds([int]$active.duration_seconds).ToString('o')
                        Save-CarouselJson $statePath $state
                        continue
                    }
                    $index = 0
                }
                $state.next_index = $index
                $minutes = if ([int]$active.min_minutes -eq [int]$active.max_minutes) {
                    [int]$active.min_minutes
                } else {
                    Get-Random -Minimum ([int]$active.min_minutes) -Maximum ([int]$active.max_minutes + 1)
                }
                $state.next_at_utc = $now.AddMinutes($minutes).ToString('o')
                Save-CarouselJson $statePath $state
            }
        } catch {
            Add-Content -LiteralPath (Join-Path $RootPath 'worker.log') -Value "$([DateTime]::UtcNow.ToString('o')) $($_.Exception.Message)"
            if ($null -ne $state) {
                $state.next_at_utc = [DateTime]::UtcNow.AddMinutes(1).ToString('o')
                Save-CarouselJson $statePath $state
            }
        }
        if (-not $DryRun) {
            [System.Windows.Forms.Application]::DoEvents()
            if ($null -ne $script:carouselForm -and
                ($script:carouselForm.IsDisposed -or [DateTime]::UtcNow -ge $script:visibleUntil)) {
                Close-CenterImage
            }
        }
        if ($Once) { break }
        # Sem janela, a próxima imagem está a minutos de distância: reduzir as
        # leituras de active.json/state.json de 5 para 1 por segundo.
        $pollMilliseconds = if ($null -ne $script:carouselForm) { 200 } else { 1000 }
        Start-Sleep -Milliseconds $pollMilliseconds
    }
} finally {
    Close-CenterImage
    $workerLock.Dispose()
}
