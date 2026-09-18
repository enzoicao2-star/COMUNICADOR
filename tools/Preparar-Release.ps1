param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $root 'dist\Comunicador.exe'
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

New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-Sha256 $destination).ToUpperInvariant()
$releaseNotes = @'
{
  "summary": "A vers\u00e3o 2.5.0 adiciona identidade permanente, administra\u00e7\u00e3o central, sincroniza\u00e7\u00e3o pelo Supabase e entregas mesmo com o painel fechado.",
  "changes": [
    "Cada computador ganhou um identificador permanente compartilhado pelo painel e receptor.",
    "Foi adicionado o admin global recuper\u00e1vel por senha, com OWNER exclusivo e edi\u00e7\u00e3o central de nomes e badges.",
    "Nomes, badges, administrador, lembretes e respostas agora s\u00e3o sincronizados pelo Supabase.",
    "O receptor entrega lembretes e respostas em segundo plano mesmo quando o painel est\u00e1 fechado.",
    "Notifica\u00e7\u00f5es e lembretes permanecem obrigat\u00f3rios; o bloqueio local se aplica somente a m\u00eddias.",
    "O admin pode definir remotamente uma imagem como papel de parede do computador escolhido.",
    "Foram adicionados controles num\u00e9ricos de dura\u00e7\u00e3o para imagem, v\u00eddeo e \u00e1udio.",
    "O editor de badges ganhou pr\u00e9via ao vivo, \u00edcone personalizado, brilho hologr\u00e1fico e aplica\u00e7\u00e3o sincronizada.",
    "A interface ganhou fundos animados, transpar\u00eancia e blur ajust\u00e1veis, velocidade configur\u00e1vel e melhor contraste no modo escuro."
  ]
}
'@ | ConvertFrom-Json
$manifest = [ordered]@{
    version = $Version
    download_url = 'https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/Comunicador.exe'
    sha256 = $hash
    summary = $releaseNotes.summary
    changes = @($releaseNotes.changes)
}
$json = $manifest | ConvertTo-Json
[IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Release local $Version preparada. SHA-256: $hash"
