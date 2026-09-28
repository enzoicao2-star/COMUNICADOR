param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$SourcePath = ''
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    Join-Path $root 'dist\Comunicador.exe'
} else {
    [IO.Path]::GetFullPath($SourcePath)
}
$releaseDirectory = Join-Path $root 'release'
$destination = Join-Path $releaseDirectory 'Comunicador.exe'
$manifestPath = Join-Path $releaseDirectory 'panel-version.json'

if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $existingManifest = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ([version]$Version -lt [version]$existingManifest.version) {
        throw 'Publique com uma versao maior que a release atual para nao substituir uma atualizacao existente.'
    }
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

if (-not (Test-Path -LiteralPath $source)) {
    throw "Executavel nao encontrado em $source. Execute build.bat primeiro."
}
$actualVersion = (Get-Item -LiteralPath $source).VersionInfo.FileVersion
if ([version]$actualVersion -ne [version]$Version) {
    throw "Versao do executavel: $actualVersion; versao pedida: $Version."
}

if ($null -ne $existingManifest -and [version]$Version -eq [version]$existingManifest.version -and
    [string]$existingManifest.sha256 -ne 'PENDING_BUILD') {
    $sourceHash = (Get-Sha256 $source).ToUpperInvariant()
    $releaseHash = if (Test-Path -LiteralPath $destination -PathType Leaf) {
        (Get-Sha256 $destination).ToUpperInvariant()
    } else { '' }
    if ($sourceHash -eq [string]$existingManifest.sha256 -and $releaseHash -eq $sourceHash) {
        Write-Host "Release local $Version ja preparada. SHA-256: $sourceHash"
        exit 0
    }
    throw 'O executavel mudou sem aumento de versao. Atualize a versao antes de publicar outra release.'
}

New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-Sha256 $destination).ToUpperInvariant()
if ($null -eq $existingManifest -or [string]::IsNullOrWhiteSpace([string]$existingManifest.summary) -or
    @($existingManifest.changes).Count -eq 0) {
    throw 'Escreva o resumo e as mudancas em release\panel-version.json antes de preparar a release.'
}
$manifest = [ordered]@{
    version = $Version
    download_url = 'https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/Comunicador.exe'
    sha256 = $hash
    summary = [string]$existingManifest.summary
    changes = @($existingManifest.changes)
}
$signature = Get-AuthenticodeSignature -LiteralPath $destination
if ($signature.Status -eq 'Valid' -and $null -ne $signature.SignerCertificate) {
    $manifest.signing_thumbprint = $signature.SignerCertificate.Thumbprint
}
$json = $manifest | ConvertTo-Json
[IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Release local $Version preparada. SHA-256: $hash"
