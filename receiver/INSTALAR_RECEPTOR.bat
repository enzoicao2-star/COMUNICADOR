@echo off
setlocal enabledelayedexpansion

rem Criar a tarefa no Agendador e liberar portas no Firewall exige administrador.
rem Se este .bat nao estiver rodando elevado, pede UAC uma unica vez e continua
rem na janela elevada (a original so passa a bola e fecha).
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Este instalador precisa de permissao de administrador para configurar o
    echo Agendador de Tarefas e o Firewall do Windows. Uma janela vai pedir sua
    echo confirmacao ^(UAC^)...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo ===============================================
echo   Comunicador Receptor - instalacao
echo ===============================================
echo.

set "REPO_RAW=https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/receiver"
set "INSTALL_DIR=%LOCALAPPDATA%\Comunicador\Receptor\app"
set "TASK_NAME=Comunicador Receptor"
set "PYTHON_INSTALLER_URL=https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.exe"
set "PYTHON_INSTALLER=%TEMP%\comunicador_python_installer.exe"
set "PYTHON_EXE="
set "PORT_TCP=57931"
set "PORT_UDP=57932"

echo [1/7] Verificando se o Python ja esta instalado...
where python >nul 2>nul
if %errorlevel%==0 (
    python -c "print(1)" >"%TEMP%\comunicador_pycheck.txt" 2>nul
    set /p PYCHECK=<"%TEMP%\comunicador_pycheck.txt"
    if "!PYCHECK!"=="1" (
        for /f "delims=" %%P in ('where python') do (
            if not defined PYTHON_EXE set "PYTHON_EXE=%%P"
        )
    )
)

if defined PYTHON_EXE (
    echo       Python encontrado em: !PYTHON_EXE!
) else (
    echo       Python nao encontrado nesta maquina.
    echo.
    echo [2/7] Baixando o instalador oficial do Python ^(python.org^)...
    curl -fsSL -o "%PYTHON_INSTALLER%" "%PYTHON_INSTALLER_URL%"
    if errorlevel 1 (
        echo ERRO: falha ao baixar o instalador do Python. Verifique sua conexao com a internet.
        goto :erro
    )

    echo       Instalando Python silenciosamente para todos os usuarios...
    "%PYTHON_INSTALLER%" /quiet InstallAllUsers=1 PrependPath=1 Include_launcher=0 Include_test=0
    if errorlevel 1 (
        echo ERRO: a instalacao do Python falhou.
        goto :erro
    )
    del "%PYTHON_INSTALLER%" >nul 2>nul

    echo       Verificando instalacao...
    set "PYTHON_EXE="
    for /d %%D in ("%ProgramFiles%\Python3*") do (
        if exist "%%D\python.exe" set "PYTHON_EXE=%%D\python.exe"
    )
    if not defined PYTHON_EXE (
        for /d %%D in ("%LOCALAPPDATA%\Programs\Python\Python3*") do (
            if exist "%%D\python.exe" set "PYTHON_EXE=%%D\python.exe"
        )
    )
    if not defined PYTHON_EXE (
        echo ERRO: nao foi possivel localizar o Python apos a instalacao.
        goto :erro
    )
    echo       Python instalado em: !PYTHON_EXE!
)

for %%F in ("!PYTHON_EXE!") do set "PYTHON_DIR=%%~dpF"
set "PYTHONW_EXE=!PYTHON_DIR!pythonw.exe"
if not exist "!PYTHONW_EXE!" (
    echo ERRO: pythonw.exe nao encontrado em "!PYTHON_DIR!".
    goto :erro
)

echo.
echo [3/7] Preparando pasta de instalacao...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

echo.
echo [4/7] Baixando receptor.py, protocolo.py e requirements.txt...
curl -fsSL -o "%INSTALL_DIR%\receptor.py" "%REPO_RAW%/receptor.py"
if errorlevel 1 goto :erro_download
curl -fsSL -o "%INSTALL_DIR%\protocolo.py" "%REPO_RAW%/protocolo.py"
if errorlevel 1 goto :erro_download
curl -fsSL -o "%INSTALL_DIR%\requirements.txt" "%REPO_RAW%/requirements.txt"
if errorlevel 1 goto :erro_download
goto :download_ok

:erro_download
echo ERRO: falha ao baixar os arquivos do receptor. Verifique sua conexao com a internet.
goto :erro

:download_ok
echo       Arquivos salvos em: %INSTALL_DIR%

echo.
echo [5/7] Instalando dependencias Python ^(pystray, Pillow^)...
"!PYTHON_EXE!" -m pip install --user --quiet --upgrade pip
"!PYTHON_EXE!" -m pip install --user --quiet -r "%INSTALL_DIR%\requirements.txt"
if errorlevel 1 (
    echo AVISO: nao foi possivel instalar todas as dependencias opcionais.
    echo        O receptor funciona normalmente, so o icone na bandeja fica desativado.
)

echo.
echo [6/7] Configurando inicializacao automatica e Firewall...
schtasks /query /tn "%TASK_NAME%" >nul 2>nul
if %errorlevel%==0 (
    echo       Tarefa existente encontrada, atualizando...
    schtasks /delete /tn "%TASK_NAME%" /f >nul 2>nul
)

schtasks /create /tn "%TASK_NAME%" /sc onlogon /rl limited ^
    /tr "\"!PYTHONW_EXE!\" \"%INSTALL_DIR%\receptor.py\"" /f
if errorlevel 1 (
    echo ERRO: falha ao criar a tarefa agendada.
    goto :erro
)
echo       Tarefa "%TASK_NAME%" criada — visivel no Agendador de Tarefas do Windows,
echo       inicia com o login do usuario atual, sem janela de console.

echo       Salvando as configuracoes atuais de rede antes de alterar...
powershell -NoProfile -Command ^
    "$bk = Join-Path $env:LOCALAPPDATA 'Comunicador\backup_rede.csv';" ^
    "if (Test-Path $bk) { Write-Host '      Backup anterior preservado (estado original ja guardado).' } else {" ^
    "  New-Item -ItemType Directory -Force -Path (Split-Path $bk) | Out-Null;" ^
    "  Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Loopback' } | ForEach-Object {" ^
    "    $p = Get-NetConnectionProfile -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue;" ^
    "    if ($p) { \"$($_.ifIndex),$($p.NetworkCategory)\" } } | Set-Content -Path $bk -Encoding UTF8;" ^
    "  Write-Host ('      Backup salvo em: ' + $bk) }"

echo       Marcando as redes fisicas como "Particular" ^(no perfil Publico o
echo       Windows bloqueia a descoberta entre computadores; adaptadores
echo       virtuais/VPN/loopback nao sao tocados^)...
powershell -NoProfile -Command ^
    "Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Loopback' } | ForEach-Object { $p = Get-NetConnectionProfile -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue; if ($p -and $p.NetworkCategory -eq 'Public') { try { Set-NetConnectionProfile -InterfaceIndex $_.ifIndex -NetworkCategory Private -ErrorAction Stop; Write-Host ('      ALTERADO: ' + $p.Name + ' Publica -> Particular') } catch {} } }"

netsh advfirewall firewall delete rule name="Comunicador Receptor" >nul 2>nul
netsh advfirewall firewall delete rule name="Comunicador Receptor (descoberta)" >nul 2>nul
rem profile=any cobre Publico/Particular/Dominio — se a rede voltar a ser
rem classificada como Publica, a regra continua valendo.
netsh advfirewall firewall add rule name="Comunicador Receptor" dir=in action=allow protocol=TCP localport=%PORT_TCP% profile=any >nul
netsh advfirewall firewall add rule name="Comunicador Receptor (descoberta)" dir=in action=allow protocol=UDP localport=%PORT_UDP% profile=any >nul
if errorlevel 1 (
    echo       AVISO: nao foi possivel liberar as portas no Firewall automaticamente.
    echo       Outros paineis podem nao conseguir encontrar este receptor pela rede.
) else (
    echo       Portas TCP %PORT_TCP% e UDP %PORT_UDP% liberadas no Firewall do Windows.
)

echo.
echo [7/7] Iniciando o receptor agora...
schtasks /run /tn "%TASK_NAME%" >nul 2>nul

echo.
echo ===============================================
echo   Instalacao concluida com sucesso!
echo   O Comunicador Receptor esta rodando em segundo plano
echo   e vai iniciar automaticamente a cada login.
echo.
echo   Para desinstalar, execute DESINSTALAR_RECEPTOR.bat
echo ===============================================
pause
exit /b 0

:erro
echo.
echo ===============================================
echo   Instalacao FALHOU. Veja os erros acima.
echo ===============================================
pause
exit /b 1
