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
    Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System.Windows.Forms;
public sealed class CarouselTrailForm : Form {
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams {
        get {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x080000A0; // no activate, no mouse input, no taskbar
            return parameters;
        }
    }
}
'@
    [System.Windows.Forms.Application]::EnableVisualStyles()
}
$script:carouselForm = $null
$script:carouselBitmap = $null
$script:visibleUntil = [DateTime]::MinValue
$script:trailForms = New-Object 'System.Collections.Generic.List[object]'
$script:trailSession = ''
$script:trailIndex = 0
$script:lastTrailPoint = $null
$script:lastTrailAt = [DateTime]::MinValue

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

function Clear-TrailImages {
    foreach ($entry in $script:trailForms) {
        if (-not $entry.Form.IsDisposed) { $entry.Form.Close() }
        $entry.Form.Dispose()
        $entry.Bitmap.Dispose()
    }
    $script:trailForms.Clear()
}

function Remove-TrailImageAt([int]$Index) {
    $entry = $script:trailForms[$Index]
    if (-not $entry.Form.IsDisposed) { $entry.Form.Close() }
    $entry.Form.Dispose()
    $entry.Bitmap.Dispose()
    $script:trailForms.RemoveAt($Index)
}

function Update-TrailImages {
    for ($index = $script:trailForms.Count - 1; $index -ge 0; $index--) {
        $entry = $script:trailForms[$index]
        if ($entry.Form.IsDisposed -or [DateTime]::UtcNow -ge $entry.Until) {
            Remove-TrailImageAt $index
            continue
        }
        $elapsed = ([DateTime]::UtcNow - $entry.Start).TotalMilliseconds
        $remaining = ($entry.Until - [DateTime]::UtcNow).TotalMilliseconds
        $progress = [Math]::Min(1, $elapsed / 300)
        $eased = 1 - [Math]::Pow(1 - $progress, 3)
        $scale = .55 + .45 * $eased
        $width = [int][Math]::Round(100 * $scale)
        $height = [int][Math]::Round(120 * $scale)
        $entry.Form.ClientSize = [System.Drawing.Size]::new($width, $height)
        $entry.Form.Location = [System.Drawing.Point]::new(
            $entry.Position.X - [int]($width / 2),
            $entry.Position.Y - [int]($height / 2))
        $entry.Form.Opacity = [Math]::Max(.01, [Math]::Min(1,
            [Math]::Min($elapsed / 240, $remaining / 260)))
        $radius = [Math]::Min(18, [int]($width / 5))
        $diameter = $radius * 2
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $path.AddArc($width - $diameter, 0, $diameter, $diameter, 270, 90)
        $path.AddArc($width - $diameter, $height - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc(0, $height - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()
        $previousRegion = $entry.Form.Region
        $entry.Form.Region = [System.Drawing.Region]::new($path)
        if ($null -ne $previousRegion) { $previousRegion.Dispose() }
        $path.Dispose()
    }
}

function Show-TrailImage([string]$Path, [System.Drawing.Point]$Position, [double]$Seconds) {
    while ($script:trailForms.Count -ge 5) { Remove-TrailImageAt 0 }
    $bitmap = [System.Drawing.Image]::FromFile($Path)
    try {
        $form = New-Object CarouselTrailForm
        $form.FormBorderStyle = 'None'
        $form.ShowInTaskbar = $false
        $form.TopMost = $true
        $form.BackColor = [System.Drawing.Color]::Fuchsia
        $form.TransparencyKey = [System.Drawing.Color]::Fuchsia
        $form.ClientSize = [System.Drawing.Size]::new(55, 66)
        $form.StartPosition = 'Manual'
        $form.Location = [System.Drawing.Point]::new($Position.X - 27, $Position.Y - 33)
        $form.Opacity = .01
        $picture = New-Object System.Windows.Forms.PictureBox
        $picture.Dock = 'Fill'
        $picture.SizeMode = 'Zoom'
        $picture.BackColor = [System.Drawing.Color]::Fuchsia
        $picture.Image = $bitmap
        $form.Controls.Add($picture)
        $form.Show()
        $script:trailForms.Add([pscustomobject]@{
            Form = $form; Bitmap = $bitmap; Position = $Position
            Start = [DateTime]::UtcNow; Until = [DateTime]::UtcNow.AddSeconds($Seconds)
        })
    } catch {
        $bitmap.Dispose()
        throw
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
            if ($active.target -notin @('center_image', 'mouse_trail')) {
                # Desativa carrosséis antigos; nenhum worker novo troca o fundo do Windows.
                $active.enabled = $false
                Save-CarouselJson $activePath $active
                if (-not $DryRun) { Remove-ItemProperty -Path $runKey -Name 'ComunicadorCarousel' -ErrorAction SilentlyContinue }
                break
            }
            if ($active.target -eq 'mouse_trail') {
                $now = [DateTime]::UtcNow
                if ([string]::IsNullOrWhiteSpace([string]$active.expires_at_utc) -or
                    $now -ge [DateTime]::Parse([string]$active.expires_at_utc).ToUniversalTime()) {
                    $active.enabled = $false
                    Save-CarouselJson $activePath $active
                    if (-not $DryRun) { Remove-ItemProperty -Path $runKey -Name 'ComunicadorCarousel' -ErrorAction SilentlyContinue }
                    # O tempo total termina a criação de imagens; as que já estão
                    # na tela ainda cumprem seu próprio tempo de exibição.
                    while (-not $DryRun -and $script:trailForms.Count -gt 0) {
                        Update-TrailImages
                        [System.Windows.Forms.Application]::DoEvents()
                        Start-Sleep -Milliseconds 50
                    }
                    break
                }
                if ($script:trailSession -ne [string]$active.session_id) {
                    Close-CenterImage
                    Clear-TrailImages
                    $script:trailSession = [string]$active.session_id
                    $script:trailIndex = 0
                    $script:lastTrailPoint = if ($DryRun) { $null } else { [System.Windows.Forms.Cursor]::Position }
                    $script:lastTrailAt = [DateTime]::MinValue
                }
                if (-not $DryRun) {
                    $position = [System.Windows.Forms.Cursor]::Position
                    $dx = $position.X - $script:lastTrailPoint.X
                    $dy = $position.Y - $script:lastTrailPoint.Y
                    $distance = [Math]::Max(24,
                        [System.Windows.Forms.Screen]::FromPoint($position).Bounds.Width / 25.0)
                    if (($dx * $dx + $dy * $dy) -ge ($distance * $distance) -and
                        ($now - $script:lastTrailAt).TotalMilliseconds -ge 110) {
                        $images = @($active.images)
                        if ($images.Count -gt 0) {
                            $fileName = [string]$images[$script:trailIndex % $images.Count]
                            if ([System.IO.Path]::GetFileName($fileName) -ne $fileName) { throw 'Nome de imagem inválido.' }
                            $imagePath = Join-Path (Join-Path $RootPath ([string]$active.folder)) $fileName
                            if (-not [System.IO.File]::Exists($imagePath)) { throw "Imagem ausente: $fileName" }
                            Show-TrailImage $imagePath $position ([double]$active.trail_image_seconds)
                            $script:trailIndex++
                            $script:lastTrailPoint = $position
                            $script:lastTrailAt = $now
                        }
                    }
                    Update-TrailImages
                }
            } else {
            if ($script:trailSession -ne '') {
                Clear-TrailImages
                $script:trailSession = ''
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
        $pollMilliseconds = if ($script:trailSession -ne '') { 50 } elseif ($null -ne $script:carouselForm) { 200 } else { 1000 }
        Start-Sleep -Milliseconds $pollMilliseconds
    }
} finally {
    Close-CenterImage
    Clear-TrailImages
    $workerLock.Dispose()
}
