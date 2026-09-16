param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [string]$ManifestUrl = 'https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/panel-version.json'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Get-VersionOrZero([string]$value) {
    try { return [version]$value }
    catch { return [version]'0.0.0.0' }
}

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
        finally { $algorithm.Dispose() }
    }
    finally { $stream.Dispose() }
}

$target = [IO.Path]::GetFullPath($ExecutablePath)
$targetDirectory = Split-Path -Parent $target
$currentVersion = [version]'0.0.0.0'
if (Test-Path -LiteralPath $target) {
    $currentVersion = Get-VersionOrZero (Get-Item -LiteralPath $target).VersionInfo.FileVersion
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $cacheBuster = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $manifest = Invoke-RestMethod -UseBasicParsing -Headers @{ 'Cache-Control' = 'no-cache' } `
        -Uri ($ManifestUrl + '?t=' + $cacheBuster)
    $latestVersion = Get-VersionOrZero ([string]$manifest.version)
    $downloadUrl = [string]$manifest.download_url
    $expectedHash = ([string]$manifest.sha256).Trim().ToUpperInvariant()
    if ($latestVersion -eq [version]'0.0.0.0' -or [string]::IsNullOrWhiteSpace($downloadUrl) `
        -or $expectedHash -notmatch '^[A-F0-9]{64}$') {
        throw 'O manifesto publicado esta incompleto.'
    }
}
catch {
    if (Test-Path -LiteralPath $target) {
        Write-Host ('Sem acesso ao GitHub; abrindo a versao instalada ' + $currentVersion + '.')
        exit 0
    }
    Write-Host ('Falha ao consultar a versao publicada: ' + $_.Exception.Message)
    exit 2
}

if ((Test-Path -LiteralPath $target) -and $currentVersion -ge $latestVersion) {
    Write-Host ('Comunicador ' + $currentVersion + ' ja esta atualizado.')
    exit 0
}

Write-Host ('Nova versao encontrada no GitHub: ' + $latestVersion + '.')
Write-Host 'Baixando e validando antes de substituir a copia instalada...'
New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
$downloadPath = Join-Path $targetDirectory 'Comunicador.download.exe'
$backupPath = Join-Path $targetDirectory 'Comunicador.anterior.exe'

try {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    Invoke-WebRequest -UseBasicParsing -Uri ($downloadUrl + '?v=' + $latestVersion) `
        -OutFile $downloadPath -ErrorAction Stop

    $actualHash = (Get-Sha256 $downloadPath).ToUpperInvariant()
    if ($actualHash -ne $expectedHash) {
        throw 'O SHA-256 do arquivo baixado nao corresponde ao manifesto.'
    }
    $downloadedVersion = Get-VersionOrZero (Get-Item -LiteralPath $downloadPath).VersionInfo.FileVersion
    if ($downloadedVersion -ne $latestVersion) {
        throw ('O arquivo baixado informa a versao ' + $downloadedVersion + ', esperada ' + $latestVersion + '.')
    }

    Get-CimInstance Win32_Process -Filter "Name='Comunicador.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -eq $target } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backupPath -Force }
    try {
        Move-Item -LiteralPath $downloadPath -Destination $target -Force
        Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
    }
    catch {
        if (Test-Path -LiteralPath $backupPath) {
            Move-Item -LiteralPath $backupPath -Destination $target -Force
        }
        throw
    }
    Write-Host ('Comunicador atualizado automaticamente para ' + $latestVersion + '.')
    exit 0
}
catch {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    Write-Host ('Falha na atualizacao automatica: ' + $_.Exception.Message)
    if (Test-Path -LiteralPath $target) { exit 0 }
    exit 3
}
