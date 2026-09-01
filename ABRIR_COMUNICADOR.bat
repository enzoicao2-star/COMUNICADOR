@echo off
setlocal

set ROOT=%~dp0
cd /d "%ROOT%"

if not exist "dist\Comunicador.exe" (
    echo O Comunicador ainda nao foi compilado. Compilando agora...
    echo.
    call build.bat
    if errorlevel 1 (
        echo.
        echo Nao foi possivel compilar o Comunicador. Veja os erros acima.
        pause
        exit /b 1
    )
)

start "" "dist\Comunicador.exe"
exit /b 0
