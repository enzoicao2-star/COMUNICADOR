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
    if (-not [string]::IsNullOrWhiteSpace([string]$existingManifest.rollback_from_version)) {
        throw 'Release 2.5.5 preservada para restaurar os PCs bloqueados. Remova rollback_from_version somente ao publicar um executavel novo que funcione nesses PCs.'
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

New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-Sha256 $destination).ToUpperInvariant()
$releaseNotes = @{
    summary = 'Um único Comunicador na bandeja, envios com prévia e reenvio, grupos e modelos compartilhados e atualização mais segura.'
    changes = @(
        'Em Mensagens, teste a prévia neste PC antes de enviar ao computador escolhido.',
        'Em Histórico, filtre os resultados por estado e reenvie mensagens que falharam.',
        'Em Mensagens, o OWNER cria grupos de computadores e modelos de texto para todos os painéis.',
        'Em Configurações > Administrador, o OWNER vê as mudanças globais e os comandos remotos recentes.',
        'A atualização restaura a versão anterior se o novo painel não confirmar a inicialização.',
        'Abrir outra cópia do painel traz a janela existente e não cria outro ícone na bandeja.',
        'Após instalar o receptor com sucesso, o instalador usado se apaga e não reaparece nas atualizações comuns.',
        'O painel passa para a versão 2.5.5.'
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
