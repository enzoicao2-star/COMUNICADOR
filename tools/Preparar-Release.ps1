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
    $existingManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
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

if ($null -ne $existingManifest -and [version]$Version -eq [version]$existingManifest.version) {
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
$releaseNotes = @{
    summary = 'Terminal remoto do OWNER com CMD comum e controles de áudio.'
    changes = @(
        'Em Gerenciar computador, o OWNER pode executar comandos CMD e ver o resultado no painel.',
        'Com volume N, audio devices e audio select N, é possível ajustar o som e escolher a saída.',
        'Os comandos expiram em 5 minutos, têm limite de 30 segundos e não pedem acesso de administrador.',
        'O receptor 2.5.6 executa os comandos mesmo quando o painel de destino está fechado.'
    )
}
$manifest = [ordered]@{
    version = $Version
    download_url = 'https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/Comunicador.exe'
    sha256 = $hash
    summary = $releaseNotes.summary
    changes = @($releaseNotes.changes)
}
$signature = Get-AuthenticodeSignature -LiteralPath $destination
if ($signature.Status -eq 'Valid' -and $null -ne $signature.SignerCertificate) {
    $manifest.signing_thumbprint = $signature.SignerCertificate.Thumbprint
}
$json = $manifest | ConvertTo-Json
[IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Release local $Version preparada. SHA-256: $hash"
