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
  "summary": "A vers\u00e3o 2.5.2 adiciona a\u00e7\u00f5es de administra\u00e7\u00e3o remota e corrige o instalador do receptor.",
  "changes": [
    "O \u00edcone Coroa agora mostra uma coroa real, em vez do carrinho de compras.",
    "As badges ganharam a op\u00e7\u00e3o Flutuar suavemente, preservada na sincroniza\u00e7\u00e3o entre pain\u00e9is.",
    "O OWNER pode conceder, retirar e personalizar a badge ADMIN; os demais usu\u00e1rios n\u00e3o podem alterar badges reservadas.",
    "GIFs animados agora s\u00e3o reproduzidos no painel e no receptor, mantendo todos os quadros e tempos da anima\u00e7\u00e3o.",
    "Ao fechar a janela, o Comunicador continua ativo na bandeja e recebe mensagens e lembretes obrigat\u00f3rios em segundo plano.",
    "O atalho de administrador com dois ap\u00f3strofos foi corrigido para layouts de teclado diferentes.",
    "A predefini\u00e7\u00e3o de cor deixou de exibir contornos encaixados e agora tem um \u00fanico cart\u00e3o.",
    "A op\u00e7\u00e3o de papel de parede remoto deixou de aparecer duplicada e fica vis\u00edvel somente para o OWNER.",
    "O menu de tr\u00eas pontos permite renomear, parear novamente, reinstalar o receptor e remover um computador da lista.",
    "O OWNER pode instalar ou reinstalar o painel em outro PC e ativar ou bloquear a entrada de conex\u00f5es locais.",
    "Comandos remotos administrativos s\u00f3 podem ser enviados pelo OWNER e t\u00eam confirma\u00e7\u00e3o pelo hist\u00f3rico de respostas.",
    "O instalador, o receptor e o painel passam a informar a vers\u00e3o 2.5.2."
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
