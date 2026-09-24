param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$Thumbprint
)

$ErrorActionPreference = 'Stop'
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
    throw 'Certificado de assinatura com chave privada nao encontrado no Windows.'
}
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
if (-not @($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq $codeSigningOid }).Count) {
    throw 'O certificado nao permite assinatura de codigo.'
}
if ($certificate.NotAfter -le [datetime]::UtcNow) { throw 'O certificado de assinatura expirou.' }

$signature = Set-AuthenticodeSignature -LiteralPath $path -Certificate $certificate `
    -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
if ($signature.Status -ne 'Valid') {
    throw ('Assinatura rejeitada: ' + $signature.StatusMessage)
}
$verified = Get-AuthenticodeSignature -LiteralPath $path
if ($verified.Status -ne 'Valid' -or $verified.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $wanted) {
    throw 'A assinatura nao passou na verificacao final.'
}
Write-Host ('Assinatura valida: ' + $verified.SignerCertificate.Subject)
