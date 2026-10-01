param(
    [string]$Version = '2.5.27.1',
    [string]$ReceiverVersion = '2.5.15.1'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root 'release\teste'
$receiverRoot = Join-Path $testRoot 'receiver'
$testBranch = 'codex/test-download-' + $Version.Replace('.', '-')
New-Item -ItemType Directory -Force -Path $receiverRoot | Out-Null

$receiver = [IO.File]::ReadAllText((Join-Path $root 'receiver\receptor.py'), [Text.Encoding]::UTF8)
$receiver = $receiver.Replace('RECEIVER_VERSION = "2.5.15"', "RECEIVER_VERSION = `"$ReceiverVersion`"")
if (-not $receiver.Contains("RECEIVER_VERSION = `"$ReceiverVersion`"")) { throw 'Nao foi possivel preparar a versao de teste do receptor.' }
$receiver = $receiver.Replace('return Path(base) / "Comunicador" / "Receptor"',
    'return Path(base) / "Comunicador-Teste" / "Receptor"')
[IO.File]::WriteAllText((Join-Path $receiverRoot 'receptor.py'), $receiver, [Text.UTF8Encoding]::new($false))

$protocol = [IO.File]::ReadAllText((Join-Path $root 'receiver\protocolo.py'), [Text.Encoding]::UTF8)
$protocol = $protocol.Replace('TCP_PORT = 57931', 'TCP_PORT = 58931')
$protocol = $protocol.Replace('UDP_DISCOVERY_PORT = 57932', 'UDP_DISCOVERY_PORT = 58932')
if (-not $protocol.Contains('TCP_PORT = 58931') -or -not $protocol.Contains('UDP_DISCOVERY_PORT = 58932')) {
    throw 'Nao foi possivel separar as portas do receptor de teste.'
}
[IO.File]::WriteAllText((Join-Path $receiverRoot 'protocolo.py'), $protocol, [Text.UTF8Encoding]::new($false))

$receiverInstaller = [IO.File]::ReadAllText((Join-Path $root 'receiver\INSTALAR_RECEPTOR.bat'), [Text.Encoding]::UTF8)
$receiverInstaller = $receiverInstaller.Replace('/main/receiver', '/main/release/teste/receiver')
$receiverInstaller = $receiverInstaller.Replace('%LOCALAPPDATA%\Comunicador\Receptor', '%LOCALAPPDATA%\Comunicador-Teste\Receptor')
$receiverInstaller = $receiverInstaller.Replace('Comunicador Receptor', 'Comunicador Receptor Teste')
$receiverInstaller = $receiverInstaller.Replace('set "PORT_TCP=57931"', 'set "PORT_TCP=58931"')
$receiverInstaller = $receiverInstaller.Replace('set "PORT_UDP=57932"', 'set "PORT_UDP=58932"')
[IO.File]::WriteAllText((Join-Path $testRoot 'INSTALAR_RECEPTOR_TESTE.bat'), $receiverInstaller, [Text.Encoding]::Default)

$firewall = @'
@echo off
netsh advfirewall firewall add rule name="Comunicador Teste" dir=in action=allow protocol=TCP localport=58933 profile=any >nul
netsh advfirewall firewall add rule name="Comunicador Teste (descoberta)" dir=in action=allow protocol=UDP localport=58934 profile=any >nul
exit /b 0
'@
[IO.File]::WriteAllText((Join-Path $testRoot 'LIBERAR_FIREWALL_TESTE.bat'), $firewall, [Text.Encoding]::ASCII)

foreach ($name in @('DIAGNOSTICO.bat', 'DESINSTALAR_RECEPTOR.bat')) {
    $path = Join-Path $root (Join-Path 'receiver' $name)
    $content = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
    $content = $content.Replace('%LOCALAPPDATA%\Comunicador\Receptor', '%LOCALAPPDATA%\Comunicador-Teste\Receptor')
    $content = $content.Replace('Comunicador Receptor', 'Comunicador Receptor Teste')
    $content = $content.Replace('2.5.15', $ReceiverVersion)
    if ($name -eq 'DIAGNOSTICO.bat') { $content = $content.Replace('57931', '58931').Replace('57932', '58932') }
    [IO.File]::WriteAllText((Join-Path $testRoot $name), $content, [Text.Encoding]::Default)
}
Copy-Item (Join-Path $root 'receiver\requirements.txt') (Join-Path $receiverRoot 'requirements.txt') -Force

$output = Join-Path $root 'dist\Teste'
dotnet publish (Join-Path $root 'src\Comunicador\Comunicador.csproj') -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:NuGetAudit=false -p:TestBuild=true `
    -o $output --no-restore
if ($LASTEXITCODE -ne 0) { throw 'A compilacao do painel de teste falhou.' }

$testExe = Join-Path $output 'Comunicador-Teste.exe'
if (-not (Test-Path -LiteralPath $testExe -PathType Leaf)) { throw 'Executavel de teste nao foi criado.' }
$actualVersion = (Get-Item -LiteralPath $testExe).VersionInfo.FileVersion
if ([version]$actualVersion -ne [version]$Version) { throw "Versao inesperada no painel de teste: $actualVersion." }
$marker = Join-Path ([IO.Path]::GetTempPath()) ('Comunicador-Teste-smoke-' + [guid]::NewGuid().ToString('N') + '.txt')
try {
    $smoke = Start-Process -FilePath $testExe -ArgumentList ('--startup-smoke-test=' + $marker) `
        -WorkingDirectory $output -WindowStyle Hidden -PassThru
    if (-not $smoke.WaitForExit(30000)) {
        Stop-Process -Id $smoke.Id -Force -ErrorAction SilentlyContinue
        throw 'O teste de abertura do painel de teste excedeu 30 segundos.'
    }
    if ($smoke.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $marker -PathType Leaf)) {
        throw "O painel de teste nao abriu corretamente (codigo $($smoke.ExitCode))."
    }
}
finally { Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue }
Copy-Item -LiteralPath $testExe -Destination (Join-Path $testRoot 'Comunicador-Teste.exe') -Force
$hash = (Get-FileHash -LiteralPath (Join-Path $testRoot 'Comunicador-Teste.exe') -Algorithm SHA256).Hash
$manifest = [ordered]@{
    version = $Version
    download_url = "https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/$testBranch/release/teste/Comunicador-Teste.exe"
    sha256 = $hash
    summary = 'Painel separado para validar receptores de teste.'
    changes = @('Usa portas e dados proprios e e sempre OWNER.', 'Mostra somente receptores da rede de teste; nao sincroniza a lista de producao.')
}
$json = $manifest | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $testRoot 'panel-version.json'), $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Painel de teste $Version criado em release\teste. SHA-256: $hash"
