$builtInSecurityModule = Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1'
if (Test-Path -LiteralPath $builtInSecurityModule -PathType Leaf) {
    Import-Module -Name $builtInSecurityModule -ErrorAction Stop
}

function Get-ComunicadorSignTool {
    $onPath = @(Get-Command signtool.exe -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty Source)[0]
    if ($onPath) { return $onPath }

    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    if ($programFilesX86) {
        $sdkBin = Join-Path $programFilesX86 'Windows Kits\10\bin'
        $installed = @(Get-ChildItem -LiteralPath $sdkBin -Directory -ErrorAction SilentlyContinue |
            Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1)[0]
        if ($installed) { return $installed }
    }
    throw 'SignTool do Windows SDK nao encontrado. Instale o Windows SDK para assinar a release.'
}

function Assert-ComunicadorSignature {
    param(
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$Thumbprint,
        [switch]$Development
    )

    $path = [IO.Path]::GetFullPath($ExecutablePath)
    $expected = ($Thumbprint -replace '[^A-Fa-f0-9]', '').ToUpperInvariant()
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $expected) {
        throw 'O executavel nao foi assinado pelo certificado configurado.'
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw 'A assinatura nao possui carimbo de data RFC 3161.'
    }

    $signTool = Get-ComunicadorSignTool
    $previousErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $verification = @(& $signTool verify /pa /all /v $path 2>&1)
        $verifyExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorPreference }
    if ($verifyExitCode -eq 0 -and $signature.Status -eq 'Valid') {
        return $signature
    }

    # Um certificado de desenvolvimento autoassinado nao tem raiz confiavel em
    # outros PCs. Aceitamos somente esse erro de cadeia; hash e carimbo precisam
    # continuar validos. Nao alteramos o armazenamento de confianca do Windows.
    $certificate = $signature.SignerCertificate
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        $null = $chain.Build($certificate)
        $chainFlags = @($chain.ChainStatus | ForEach-Object { $_.Status })
    }
    finally { $chain.Dispose() }
    $verifyText = $verification -join [Environment]::NewLine
    $onlyUntrustedRoot = $chainFlags.Count -eq 1 -and
        $chainFlags[0] -eq [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot
    if ($Development -and $certificate.Subject -eq $certificate.Issuer -and
        $signature.Status -eq 'UnknownError' -and $onlyUntrustedRoot -and
        $verifyExitCode -eq 1 -and
        $verifyText -match 'SignTool Error: A certificate chain processed, but terminated in a root' -and
        $verifyText -match 'certificate which is not trusted by the trust provider' -and
        $verifyText -match 'Number of errors:\s*1') {
        return $signature
    }
    throw ('Assinatura invalida ou nao confiavel: ' + $signature.StatusMessage +
        [Environment]::NewLine + $verifyText)
}
