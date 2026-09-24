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

$signTool = @(Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)[0]
if (-not $signTool) {
    $sdkBin = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Windows Kits\10\bin'
    $signTool = @(Get-ChildItem -LiteralPath $sdkBin -Directory -ErrorAction SilentlyContinue |
        Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1)[0]
}
if (-not $signTool) { throw 'SignTool do Windows SDK nao encontrado. Instale o Windows SDK para assinar a release.' }

$storeArgs = if ($certificate.PSParentPath -like '*LocalMachine*') { @('/sm') } else { @() }
$signOutput = & $signTool sign /sha1 $wanted /s My @storeArgs /fd SHA256 `
    /tr 'http://timestamp.digicert.com' /td SHA256 /v $path 2>&1
if ($LASTEXITCODE -ne 0) {
    throw ('Falha ao assinar ou carimbar a data: ' + ($signOutput -join [Environment]::NewLine))
}
$verifyOutput = & $signTool verify /pa /all /v $path 2>&1
if ($LASTEXITCODE -ne 0) {
    throw ('A assinatura nao passou na verificacao de confianca: ' + ($verifyOutput -join [Environment]::NewLine))
}
$verified = Get-AuthenticodeSignature -LiteralPath $path
if ($verified.Status -ne 'Valid' -or $verified.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $wanted) {
    throw 'A assinatura nao passou na verificacao final.'
}
Write-Host ('Assinatura valida: ' + $verified.SignerCertificate.Subject)
