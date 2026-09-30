@echo off
setlocal
set "INSTALL_ROOT=%USERPROFILE%\Documents\P5"
set "LOCAL_COPY=%TEMP%\DESINSTALAR_COMUNICADOR.bat"
if /I not "%~1"=="--executar" (
    copy /y "%~f0" "%LOCAL_COPY%" >nul
    if errorlevel 1 (echo ERRO: nao foi possivel preparar o desinstalador.& pause& exit /b 1)
    start "" "%LOCAL_COPY%" --executar
    exit /b 0
)
echo Desinstalando o Comunicador...
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
if not exist "%LOCALAPPDATA%\Comunicador\Receptor" del /q "%LOCALAPPDATA%\Comunicador\device.json" "%LOCALAPPDATA%\Comunicador\cloud_session.json" >nul 2>nul
netsh advfirewall firewall delete rule name="Comunicador" >nul 2>nul
echo Comunicador desinstalado. O receptor foi preservado.
timeout /t 5 /nobreak >nul
exit /b 0
