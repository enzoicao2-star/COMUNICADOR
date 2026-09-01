@echo off
setlocal enabledelayedexpansion

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

    echo       Instalando Python silenciosamente para o usuario atual...
    "%PYTHON_INSTALLER%" /quiet InstallAllUsers=0 PrependPath=1 Include_launcher=0 Include_test=0
    if errorlevel 1 (
        echo ERRO: a instalacao do Python falhou.
        goto :erro
    )
    del "%PYTHON_INSTALLER%" >nul 2>nul

    echo       Verificando instalacao...
    set "PYTHON_EXE="
    for /d %%D in ("%LOCALAPPDATA%\Programs\Python\Python3*") do (
        if exist "%%D\python.exe" set "PYTHON_EXE=%%D\python.exe"
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
echo [6/7] Configurando inicializacao automatica no Agendador de Tarefas...
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
