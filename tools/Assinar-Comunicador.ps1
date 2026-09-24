param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$Thumbprint,
    [switch]$Development
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assinatura-Comunicador.ps1')
$path = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw 'Executavel para assinatura nao encontrado.'
}

$wanted = ($Thumbprint -replace '[^A-Fa-f0-9]', '').ToUpperInvariant()
if ($wanted -notmatch '^[A-F0-9]{40}$') { throw 'Thumbprint do certificado invalido.' }
$certificate = @(Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint.ToUpperInvariant() -eq $wanted -and $_.HasPrivateKey } |
    Select-Object -First 1)[0]
if ($null -eq $certificate) {
    throw ('Certificado de assinatura com chave privada nao encontrado no Windows: ' + $wanted)
}
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
if (-not @($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq $codeSigningOid }).Count) {
    throw 'O certificado nao permite assinatura de codigo.'
}
if ($certificate.NotAfter -le [datetime]::UtcNow) { throw 'O certificado de assinatura expirou.' }
if ($Development -and $certificate.Subject -ne $certificate.Issuer) {
    throw 'O modo de desenvolvimento exige um certificado autoassinado.'
}

$signTool = Get-ComunicadorSignTool

$storeArgs = if ($certificate.PSParentPath -like '*LocalMachine*') { @('/sm') } else { @() }
$previousErrorPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $signOutput = & $signTool sign /sha1 $wanted /s My @storeArgs /fd SHA256 `
        /tr 'http://timestamp.digicert.com' /td SHA256 /v $path 2>&1
    $signExitCode = $LASTEXITCODE
}
finally { $ErrorActionPreference = $previousErrorPreference }
if ($signExitCode -ne 0) {
    throw ('Falha ao assinar ou carimbar a data: ' + ($signOutput -join [Environment]::NewLine))
}
$verified = Assert-ComunicadorSignature -ExecutablePath $path -Thumbprint $wanted -Development:$Development
$mode = if ($Development -and $verified.Status -ne 'Valid') { 'desenvolvimento (raiz nao confiavel)' } else { 'confiavel' }
Write-Host ('Assinatura ' + $mode + ': ' + $verified.SignerCertificate.Subject)
