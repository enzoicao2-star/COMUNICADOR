param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$SourcePath = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assinatura-Comunicador.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    Join-Path $root 'dist\Comunicador.exe'
} else {
    [IO.Path]::GetFullPath($SourcePath)
}
$releaseDirectory = Join-Path $root 'release'
$destination = Join-Path $releaseDirectory 'Comunicador.exe'
$manifestPath = Join-Path $releaseDirectory 'panel-version.json'

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

$requestedSigner = ([string]$env:COMUNICADOR_SIGNING_THUMBPRINT -replace '[^A-Fa-f0-9]', '').ToUpperInvariant()
$developmentSigning = [string]$env:COMUNICADOR_SIGNING_MODE -eq 'development'
if ($requestedSigner -and $requestedSigner -notmatch '^[A-F0-9]{40}$') {
    throw 'COMUNICADOR_SIGNING_THUMBPRINT deve conter o thumbprint SHA-1 de 40 caracteres do certificado.'
}
if ($developmentSigning -and -not $requestedSigner) {
    throw 'O modo de desenvolvimento exige COMUNICADOR_SIGNING_THUMBPRINT.'
}
$sourceSignature = Get-AuthenticodeSignature -LiteralPath $source
if ($requestedSigner) {
    try {
        $sourceSignature = Assert-ComunicadorSignature -ExecutablePath $source `
            -Thumbprint $requestedSigner -Development:$developmentSigning
    }
    catch { throw ('O executavel nao possui a assinatura esperada. Release preservada: ' + $_.Exception.Message) }
}
elseif ($sourceSignature.Status -eq 'Valid') {
    $sourceSignature = Assert-ComunicadorSignature -ExecutablePath $source `
        -Thumbprint $sourceSignature.SignerCertificate.Thumbprint
}
elseif ($sourceSignature.Status -ne 'NotSigned') {
    throw ('Assinatura do executavel invalida: ' + $sourceSignature.StatusMessage)
}

New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-Sha256 $destination).ToUpperInvariant()
$releaseNotes = @{
    summary = 'Executável do Comunicador assinado com certificado de desenvolvimento.'
    changes = @(
        'O painel 2.5.6 traz assinatura digital de desenvolvimento e carimbo de data.',
        'A confiança neste certificado deve ser configurada manualmente em cada PC; o Windows ainda pode mostrar avisos.'
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
if ($signature.Status -ne $sourceSignature.Status -or
    ($null -ne $sourceSignature.SignerCertificate -and
        $signature.SignerCertificate.Thumbprint -ne $sourceSignature.SignerCertificate.Thumbprint)) {
    throw 'A assinatura mudou durante a copia da release.'
}
if ($requestedSigner) {
    $signature = Assert-ComunicadorSignature -ExecutablePath $destination `
        -Thumbprint $requestedSigner -Development:$developmentSigning
}
if ($null -ne $signature.SignerCertificate) {
    $manifest.signing_thumbprint = $signature.SignerCertificate.Thumbprint
    $manifest.signing_mode = if ($developmentSigning) { 'development' } else { 'trusted' }
}
$json = $manifest | ConvertTo-Json
[IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Release local $Version preparada. SHA-256: $hash"
