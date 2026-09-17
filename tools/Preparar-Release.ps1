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
  "summary": "A vers\u00e3o 2.4 adiciona identidade sincronizada entre pain\u00e9is, badges personaliz\u00e1veis, atualiza\u00e7\u00e3o pelo pr\u00f3prio aplicativo e novos formatos de mensagem.",
  "changes": [
    "Foi adicionado 1 novo fundo: Onda de part\u00edculas.",
    "Foram adicionadas badges com texto, cor, estilo, \u00edcone, brilho, anima\u00e7\u00e3o e \u00edcone personalizado.",
    "Nomes p\u00fablicos, OWNER, badges, vers\u00f5es e hist\u00f3rico passam a ser sincronizados entre os pain\u00e9is.",
    "Mensagens podem aparecer no centro, usar at\u00e9 2 bot\u00f5es lado a lado e registrar a resposta no hist\u00f3rico por computador.",
    "O painel agora verifica e instala atualiza\u00e7\u00f5es sem depender da abertura pelo arquivo BAT.",
    "Foram adicionados envio de v\u00eddeo e \u00e1udio, com repeti\u00e7\u00e3o e dura\u00e7\u00e3o configur\u00e1veis."
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
