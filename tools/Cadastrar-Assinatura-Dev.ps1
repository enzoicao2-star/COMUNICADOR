param(
    [switch]$NovaChave,
    [string]$Root = (Join-Path $PSScriptRoot '..'),
    [switch]$SkipUserEnvironment
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assinatura-Comunicador.ps1')

$projectRoot = [IO.Path]::GetFullPath($Root)
$manifestPath = Join-Path $projectRoot 'release\panel-version.json'
$projectPath = Join-Path $projectRoot 'src\Comunicador\Comunicador.csproj'
$publicPath = Join-Path $projectRoot 'release\Comunicador-Dev.cer'
$localConfigPath = Join-Path $projectRoot 'tools\Assinatura-Dev.local.cmd'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw 'Projeto Comunicador incompleto. Atualize o repositorio antes de cadastrar a chave.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$publishedVersion = [version]$manifest.version
$projectText = [IO.File]::ReadAllText($projectPath, [Text.Encoding]::UTF8)
$versionMatch = [regex]::Match($projectText, '<Version>(\d+\.\d+\.\d+)</Version>')
if (-not $versionMatch.Success) { throw 'Versao do projeto nao encontrada.' }
$sourceVersion = [version]($versionMatch.Groups[1].Value + '.0')
if ($sourceVersion -lt $publishedVersion) {
    throw 'A fonte e mais antiga que a release publicada. Atualize o repositorio primeiro.'
}

$subject = 'CN=Comunicador Dev, O=Uso Interno'
$configuredThumbprint = [Environment]::GetEnvironmentVariable('COMUNICADOR_SIGNING_THUMBPRINT', 'User')
$available = @(Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending)
$certificate = $null
if (-not $NovaChave -and $configuredThumbprint) {
    $certificate = @($available | Where-Object { $_.Thumbprint -eq $configuredThumbprint } |
        Select-Object -First 1)[0]
}
if (-not $NovaChave -and $null -eq $certificate) {
    $certificate = @($available | Select-Object -First 1)[0]
}
$created = $false
if ($null -eq $certificate) {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
        -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 `
        -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(2)
    $created = $true
}
if (-not $certificate.HasPrivateKey -or
    -not @($certificate.EnhancedKeyUsageList | Where-Object {
        $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }).Count) {
    throw 'A nova chave nao pode ser usada para assinatura de codigo.'
}

$signerChanged = [string]$manifest.signing_thumbprint -ne $certificate.Thumbprint
$nextVersion = $sourceVersion
$versionUpdates = @{}
if ($signerChanged -and $sourceVersion -eq $publishedVersion) {
    $oldShort = "$($publishedVersion.Major).$($publishedVersion.Minor).$($publishedVersion.Build)"
    $newShort = "$($publishedVersion.Major).$($publishedVersion.Minor).$($publishedVersion.Build + 1)"
    $nextVersion = [version]($newShort + '.0')
    $pattern = '(?<!\d)' + [regex]::Escape($oldShort) + '(?!\d)'
    foreach ($relative in @(
        'src\Comunicador\Comunicador.csproj',
        'src\Comunicador\Protocol\ProtocolConstants.cs',
        'build.bat',
        'SUBIR_GITHUB.bat',
        'ABRIR_COMUNICADOR.bat',
        'README.md',
        'tools\Preparar-Release.ps1'
    )) {
        $path = Join-Path $projectRoot $relative
        $original = [IO.File]::ReadAllBytes($path)
        $hasBom = $original.Length -ge 3 -and $original[0] -eq 239 -and
            $original[1] -eq 187 -and $original[2] -eq 191
        $content = [Text.Encoding]::UTF8.GetString($original, $(if ($hasBom) { 3 } else { 0 }),
            $original.Length - $(if ($hasBom) { 3 } else { 0 }))
        if (-not [regex]::IsMatch($content, $pattern)) {
            throw "Versao $oldShort nao encontrada em $relative. Nenhum arquivo foi alterado."
        }
        $versionUpdates[$path] = [pscustomobject]@{
            Original = $original
            Updated = [regex]::Replace($content, $pattern, $newShort)
            HasBom = $hasBom
        }
    }
}

$temporaryPublic = Join-Path $env:TEMP ('Comunicador-Dev-' + [guid]::NewGuid().ToString('N') + '.cer')
$oldPublic = if (Test-Path -LiteralPath $publicPath) { [IO.File]::ReadAllBytes($publicPath) } else { $null }
$oldLocalConfig = if (Test-Path -LiteralPath $localConfigPath) { [IO.File]::ReadAllBytes($localConfigPath) } else { $null }
try {
    Export-Certificate -Cert $certificate -FilePath $temporaryPublic -Force | Out-Null
    $public = [Security.Cryptography.X509Certificates.X509Certificate2]::new($temporaryPublic)
    if ($public.HasPrivateKey -or $public.Thumbprint -ne $certificate.Thumbprint) {
        throw 'O certificado publico exportado nao corresponde a nova chave.'
    }

    try {
        foreach ($entry in $versionUpdates.GetEnumerator()) {
            [IO.File]::WriteAllText($entry.Key, $entry.Value.Updated,
                [Text.UTF8Encoding]::new($entry.Value.HasBom))
        }
        Copy-Item -LiteralPath $temporaryPublic -Destination $publicPath -Force
        $localConfig = "@echo off`r`n" +
            "set `"COMUNICADOR_SIGNING_THUMBPRINT=$($certificate.Thumbprint)`"`r`n" +
            "set `"COMUNICADOR_SIGNING_MODE=development`"`r`n"
        [IO.File]::WriteAllText($localConfigPath, $localConfig, [Text.Encoding]::ASCII)
        if (-not $SkipUserEnvironment) {
            [Environment]::SetEnvironmentVariable('COMUNICADOR_SIGNING_THUMBPRINT',
                $certificate.Thumbprint, 'User')
            [Environment]::SetEnvironmentVariable('COMUNICADOR_SIGNING_MODE', 'development', 'User')
        }
    }
    catch {
        foreach ($entry in $versionUpdates.GetEnumerator()) {
            [IO.File]::WriteAllBytes($entry.Key, $entry.Value.Original)
        }
        if ($null -eq $oldPublic) { Remove-Item -LiteralPath $publicPath -Force -ErrorAction SilentlyContinue }
        else { [IO.File]::WriteAllBytes($publicPath, $oldPublic) }
        if ($null -eq $oldLocalConfig) { Remove-Item -LiteralPath $localConfigPath -Force -ErrorAction SilentlyContinue }
        else { [IO.File]::WriteAllBytes($localConfigPath, $oldLocalConfig) }
        throw
    }
}
finally { Remove-Item -LiteralPath $temporaryPublic -Force -ErrorAction SilentlyContinue }

Write-Host ('Chave de desenvolvimento ' + $(if ($created) { 'criada' } else { 'cadastrada' }) +
    ': ' + $certificate.Thumbprint)
Write-Host ('Certificado publico: ' + $publicPath)
if ($signerChanged) {
    Write-Host ('A versao ' + $nextVersion + ' esta preparada para a nova chave.')
    Write-Host 'Execute SUBIR_GITHUB.bat para compilar, testar e publicar o painel assinado.'
} else {
    Write-Host 'A chave ja corresponde a release publicada; builds futuras usarao esta chave.'
}
