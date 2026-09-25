param(
    [string]$RootPath = (Join-Path $env:LOCALAPPDATA 'Comunicador\Carousel'),
    [switch]$Once,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $RootPath | Out-Null
$lockPath = Join-Path $RootPath 'worker.lock'
try {
    $workerLock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
} catch [System.IO.IOException] {
    exit 0 # Another instance already owns the carousel.
}

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

function Set-LockScreenImage([string]$Path) {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $storage = [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
    $lockScreen = [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime]
    $methods = [System.WindowsRuntimeSystemExtensions].GetMethods()
    $loadMethod = $methods | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
    $loadTask = $loadMethod.MakeGenericMethod($storage).Invoke($null, @($storage::GetFileFromPathAsync($Path)))
    $loadTask.Wait()
    $applyMethod = $methods | Where-Object { $_.Name -eq 'AsTask' -and -not $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
    $applyTask = $applyMethod.Invoke($null, @($lockScreen::SetImageFileAsync($loadTask.Result)))
    $applyTask.Wait()
}

function Set-WallpaperImage([string]$Path) {
    if (-not ('Carousel.Desktop' -as [type])) {
        Add-Type -Namespace Carousel -Name Desktop -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)]
public static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);
'@
    }
    if (-not [Carousel.Desktop]::SystemParametersInfo(0x14, 0, $Path, 3)) {
        throw [System.ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }
}

function Apply-CarouselImage([string]$Path, [string]$Target) {
    if ($DryRun) {
        Add-Content -LiteralPath (Join-Path $RootPath 'applied.log') -Value "$Target|$Path"
        return
    }
    if ($Target -eq 'wallpaper' -or $Target -eq 'both') { Set-WallpaperImage $Path }
    if ($Target -eq 'lock_screen' -or $Target -eq 'both') { Set-LockScreenImage $Path }
}

$activePath = Join-Path $RootPath 'active.json'
$statePath = Join-Path $RootPath 'state.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
try {
    while ($true) {
        try {
            $active = Read-CarouselJson $activePath
            if ($null -eq $active -or -not $active.enabled) { break }
            $state = Read-CarouselJson $statePath
            if ($null -eq $state -or $state.session_id -ne $active.session_id) {
                $state = [pscustomobject]@{ session_id = $active.session_id; next_index = 0; next_at_utc = '' }
            }
            $now = [DateTime]::UtcNow
            $due = $state.next_at_utc -eq '' -or $now -ge [DateTime]::Parse($state.next_at_utc).ToUniversalTime()
            if ($due) {
                $index = [int]$state.next_index
                if ($index -ge @($active.images).Count) { break }
                $fileName = [string]$active.images[$index]
                if ([System.IO.Path]::GetFileName($fileName) -ne $fileName) { throw 'Nome de imagem inválido.' }
                $imagePath = Join-Path (Join-Path $RootPath ([string]$active.folder)) $fileName
                if (-not [System.IO.File]::Exists($imagePath)) { throw "Imagem ausente: $fileName" }
                Apply-CarouselImage $imagePath ([string]$active.target)
                $index++
                if ($index -ge @($active.images).Count) {
                    if (-not $active.repeat) {
                        $state.next_index = $index
                        $state.next_at_utc = ''
                        Save-CarouselJson $statePath $state
                        $current = Read-CarouselJson $activePath
                        if ($current.session_id -eq $active.session_id) {
                            $active.enabled = $false
                            Save-CarouselJson $activePath $active
                            if (-not $DryRun) { Remove-ItemProperty -Path $runKey -Name 'ComunicadorCarousel' -ErrorAction SilentlyContinue }
                        }
                        break
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
        if ($Once) { break }
        Start-Sleep -Seconds 1
    }
} finally {
    $workerLock.Dispose()
}
