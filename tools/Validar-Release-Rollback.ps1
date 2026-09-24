$ErrorActionPreference = 'Stop'
$securityModule = Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1'
if (Test-Path -LiteralPath $securityModule -PathType Leaf) {
    Import-Module -Name $securityModule -ErrorAction Stop
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content -LiteralPath (Join-Path $root 'release\panel-version.json') -Raw | ConvertFrom-Json
$executable = Join-Path $root 'release\Comunicador.exe'
$knownHash = '32D69DC2DAA6B2DE4D343337AE0D0502C76F3C795E45550A89C8AEB89EC27D24'

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
        finally { $algorithm.Dispose() }
    }
    finally { $stream.Dispose() }
}

if ([string]$manifest.version -ne '2.5.5.0' -or
    [string]$manifest.rollback_from_version -ne '2.5.6.0' -or
    [string]$manifest.sha256 -ne $knownHash) {
    throw 'O manifesto de reversao nao corresponde ao executavel compativel.'
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or
    (Get-Item -LiteralPath $executable).VersionInfo.FileVersion -ne '2.5.5.0' -or
    (Get-AuthenticodeSignature -LiteralPath $executable).Status -ne 'NotSigned' -or
    (Get-Sha256 $executable) -ne $knownHash) {
    throw 'O executavel antigo foi alterado. A publicacao foi interrompida.'
}
Write-Host 'Release 2.5.5 sem assinatura validada pelo hash conhecido.'
