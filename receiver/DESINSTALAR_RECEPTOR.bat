@echo off
setlocal enabledelayedexpansion

echo ===============================================
echo   Comunicador Receptor - desinstalacao
echo ===============================================
echo.

set "TASK_NAME=Comunicador Receptor"
set "INSTALL_ROOT=%LOCALAPPDATA%\Comunicador\Receptor"

echo [1/3] Parando o receptor, se estiver rodando...
powershell -NoProfile -Command ^
    "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*receptor.py*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"

echo.
echo [2/3] Removendo a tarefa do Agendador de Tarefas do Windows...
schtasks /query /tn "%TASK_NAME%" >nul 2>nul
if %errorlevel%==0 (
    schtasks /delete /tn "%TASK_NAME%" /f >nul 2>nul
    echo       Tarefa "%TASK_NAME%" removida.
) else (
    echo       Nenhuma tarefa "%TASK_NAME%" encontrada.
)

echo.
echo [3/3] Removendo arquivos instalados...
if exist "%INSTALL_ROOT%" (
    rmdir /s /q "%INSTALL_ROOT%"
    echo       Pasta removida: %INSTALL_ROOT%
) else (
    echo       Nada para remover em: %INSTALL_ROOT%
)

echo.
echo ===============================================
echo   Comunicador Receptor desinstalado.
echo   O Python em si NAO foi removido da maquina.
echo ===============================================
pause
exit /b 0
