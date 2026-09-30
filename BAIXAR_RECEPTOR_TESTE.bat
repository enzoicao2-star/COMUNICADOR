@echo off
setlocal
set "INSTALLER=%TEMP%\INSTALAR_RECEPTOR_TESTE.bat"
curl.exe -fsSL "https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/teste/INSTALAR_RECEPTOR_TESTE.bat" -o "%INSTALLER%"
if errorlevel 1 (
    echo Nao foi possivel baixar o instalador do receptor de teste.
    pause
    exit /b 1
)
start "" "%INSTALLER%"
exit /b 0
