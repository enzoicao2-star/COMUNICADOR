@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
set "COMUNICADOR_ROOT=%~dp0"
set "VERSAO_ESPERADA=2.2.2.0"
set "REPO_RAW=https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main"
cd /d "%ROOT%"

rem Um clone Git e tratado como ambiente de desenvolvimento. Se este BAT estiver
rem sozinho em outro PC, o projeto completo sera instalado em Documents\P5.
set "MODO_DEV=0"
if exist "%ROOT%.git" set "MODO_DEV=1"
if "!MODO_DEV!"=="1" (
    set "PAINEL_EXE=%ROOT%dist\Comunicador.exe"
) else (
    set "INSTALL_ROOT=%USERPROFILE%\Documents\P5"
    set "PAINEL_EXE=!INSTALL_ROOT!\release\Comunicador.exe"
)

echo Verificando o Comunicador...

rem Em uma copia de desenvolvimento, recompila apenas se a fonte local mudou ou
rem se o executavel e mais antigo que a versao desta copia. Um EXE mais novo,
rem baixado do GitHub, nunca e rebaixado por este teste.
if "!MODO_DEV!"=="1" (
    set "PRECISA_COMPILAR=0"
    if not exist "!PAINEL_EXE!" set "PRECISA_COMPILAR=1"
    if exist "!PAINEL_EXE!" (
        for /f "delims=" %%I in ('powershell -NoProfile -ExecutionPolicy Bypass -Command ^
            "$r=$env:COMUNICADOR_ROOT; $exe=$env:PAINEL_EXE; $expected=[version]$env:VERSAO_ESPERADA;" ^
            "$pastas=@((Join-Path $r 'src\Comunicador'),(Join-Path $r 'receiver'),(Join-Path $r 'tests'),(Join-Path $r 'tools'),(Join-Path $r 'assets'));" ^
            "$novo=(Get-ChildItem -Path $pastas -Recurse -File -Include *.cs,*.xaml,*.csproj,*.py,*.bat,*.ps1,*.svg -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc;" ^
            "$item=Get-Item -LiteralPath $exe; try{$atual=[version]$item.VersionInfo.FileVersion}catch{$atual=[version]'0.0.0.0'};" ^
            "if($atual -lt $expected -or $novo -gt $item.LastWriteTimeUtc){'1'}else{'0'}"') do set "PRECISA_COMPILAR=%%I"
    )
    if "!PRECISA_COMPILAR!"=="1" (
        echo A fonte local mudou. Compilando a versao 2.2.2...
        call "%ROOT%build.bat"
        if errorlevel 1 (
            echo.
            echo Nao foi possivel compilar a copia local. Tentando a versao publicada...
        )
    )
)

rem Baixa sempre o atualizador mais recente. Se a internet estiver indisponivel,
rem usa a copia que veio com o repositorio e abre o EXE instalado normalmente.
set "UPDATER=%TEMP%\Comunicador-Atualizar-Painel.ps1"
set "UPDATER_NOVO=%TEMP%\Comunicador-Atualizar-Painel.download.ps1"
del /q "!UPDATER_NOVO!" >nul 2>nul
if exist "%ROOT%tools\Atualizar-Comunicador.ps1" copy /y "%ROOT%tools\Atualizar-Comunicador.ps1" "!UPDATER!" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ProgressPreference='SilentlyContinue'; try {" ^
    "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;" ^
    "Invoke-WebRequest -UseBasicParsing -Headers @{'Cache-Control'='no-cache'} -Uri ('%REPO_RAW%/tools/Atualizar-Comunicador.ps1?t=' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -OutFile $env:UPDATER_NOVO -ErrorAction Stop" ^
    "} catch { exit 1 }"
if not errorlevel 1 move /y "!UPDATER_NOVO!" "!UPDATER!" >nul

if exist "!UPDATER!" (
    if "!MODO_DEV!"=="1" (
        powershell -NoProfile -ExecutionPolicy Bypass -File "!UPDATER!" -ExecutablePath "!PAINEL_EXE!"
    ) else (
        powershell -NoProfile -ExecutionPolicy Bypass -File "!UPDATER!" -ExecutablePath "!PAINEL_EXE!" -InstallRoot "!INSTALL_ROOT!" -LauncherPath "%~f0"
    )
    if errorlevel 1 (
        echo AVISO: a verificacao online falhou.
        if not exist "!PAINEL_EXE!" goto :sem_executavel
        echo Abrindo a copia instalada.
    )
) else (
    echo AVISO: nao foi possivel carregar o atualizador online.
    if not exist "!PAINEL_EXE!" goto :sem_executavel
)

if not exist "!PAINEL_EXE!" goto :sem_executavel

if /I "%~1"=="--verificar" (
    echo Verificacao concluida sem abrir a janela.
    exit /b 0
)

rem Na primeira execucao configura rede e Firewall. A instalacao completa ja
rem inclui o auxiliar; o download isolado abaixo e apenas uma recuperacao extra.
netsh advfirewall firewall show rule name="Comunicador" >nul 2>nul
if errorlevel 1 (
    if "!MODO_DEV!"=="1" (
        set "FIREWALL_HELPER=%ROOT%LIBERAR_FIREWALL.bat"
    ) else (
        set "FIREWALL_HELPER=!INSTALL_ROOT!\LIBERAR_FIREWALL.bat"
    )
    if not exist "!FIREWALL_HELPER!" (
        powershell -NoProfile -ExecutionPolicy Bypass -Command ^
            "$ProgressPreference='SilentlyContinue'; try {" ^
            "$dest=$env:FIREWALL_HELPER; New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null;" ^
            "Invoke-WebRequest -UseBasicParsing -Uri ('%REPO_RAW%/LIBERAR_FIREWALL.bat?t=' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -OutFile $dest -ErrorAction Stop" ^
            "} catch { exit 1 }"
    )
    if exist "!FIREWALL_HELPER!" (
        echo Configurando a rede para o Comunicador funcionar entre computadores...
        call "!FIREWALL_HELPER!"
    ) else (
        echo AVISO: nao foi possivel baixar o auxiliar do Firewall.
    )
)

start "" "!PAINEL_EXE!"
exit /b 0

:sem_executavel
echo.
echo ERRO: nao foi possivel instalar o Comunicador em:
echo       %USERPROFILE%\Documents\P5
echo       Verifique a conexao com a internet e tente novamente.
pause
exit /b 1
