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

rem Na primeira execucao configura rede e Firewall (pede UAC uma vez).
rem Depois disso a regra ja existe e o painel abre direto, sem prompt.
netsh advfirewall firewall show rule name="Comunicador" >nul 2>nul
if %errorlevel% neq 0 (
    echo Configurando a rede para o Comunicador funcionar entre computadores...
    echo Uma janela do Windows vai pedir sua confirmacao ^(UAC^) — isso acontece
    echo so nesta primeira vez.
    echo.
    call "%ROOT%LIBERAR_FIREWALL.bat"
)

start "" "dist\Comunicador.exe"
exit /b 0
