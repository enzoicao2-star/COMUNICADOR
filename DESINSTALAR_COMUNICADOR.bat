@echo off
setlocal
set "INSTALL_ROOT=%USERPROFILE%\Documents\P5"
set "LOCAL_COPY=%TEMP%\DESINSTALAR_COMUNICADOR.bat"
set "MARKER_SOURCE=%~dp0MARCAR_PAINEL_DESINSTALADO.ps1"
set "MARKER_COPY=%TEMP%\MARCAR_PAINEL_DESINSTALADO.ps1"
if /I not "%~1"=="--executar" (
    if not exist "%MARKER_SOURCE%" (echo ERRO: arquivo de sinalizacao da desinstalacao nao encontrado.& pause& exit /b 1)
    copy /y "%~f0" "%LOCAL_COPY%" >nul
    if errorlevel 1 (echo ERRO: nao foi possivel preparar o desinstalador.& pause& exit /b 1)
    copy /y "%MARKER_SOURCE%" "%MARKER_COPY%" >nul
    if errorlevel 1 (echo ERRO: nao foi possivel preparar a sinalizacao da desinstalacao.& pause& exit /b 1)
    start "" "%LOCAL_COPY%" --executar
    exit /b 0
)
echo Desinstalando o Comunicador...
powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "%MARKER_COPY%" >nul 2>nul
taskkill /IM Comunicador.exe /F >nul 2>nul
del /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Comunicador.lnk" >nul 2>nul
del /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Comunicador.lnk" >nul 2>nul
if exist "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Comunicador" rmdir /s /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Comunicador"
if exist "%INSTALL_ROOT%" rmdir /s /q "%INSTALL_ROOT%"
if exist "%INSTALL_ROOT%" (
    echo ERRO: ainda ha arquivos em "%INSTALL_ROOT%". Feche o Comunicador e tente novamente.
    pause
    exit /b 1
)
if exist "%APPDATA%\Comunicador" rmdir /s /q "%APPDATA%\Comunicador"
netsh advfirewall firewall delete rule name="Comunicador" >nul 2>nul
if exist "%LOCALAPPDATA%\Comunicador\Receptor" (echo Painel desinstalado. O receptor foi preservado.) else (echo Painel desinstalado. A identidade foi preservada para evitar um cartao duplicado.)
del /q "%MARKER_COPY%" >nul 2>nul
timeout /t 5 /nobreak >nul
exit /b 0
