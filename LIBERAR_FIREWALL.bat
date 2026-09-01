@echo off
setlocal

rem Libera as portas do Comunicador no Firewall do Windows. Precisa de
rem administrador, entao pede UAC uma vez e continua na janela elevada.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Esta configuracao precisa de permissao de administrador. Uma janela do
    echo Windows vai pedir sua confirmacao ^(UAC^)...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "PORT_TCP=57931"
set "PORT_UDP=57932"

echo ===============================================
echo   Comunicador - liberar portas no Firewall
echo ===============================================
echo.

netsh advfirewall firewall delete rule name="Comunicador" >nul 2>nul
netsh advfirewall firewall delete rule name="Comunicador (descoberta)" >nul 2>nul

netsh advfirewall firewall add rule name="Comunicador" dir=in action=allow protocol=TCP localport=%PORT_TCP% profile=private,domain >nul
netsh advfirewall firewall add rule name="Comunicador (descoberta)" dir=in action=allow protocol=UDP localport=%PORT_UDP% profile=private,domain >nul

if errorlevel 1 (
    echo ERRO: nao foi possivel criar as regras no Firewall.
    pause
    exit /b 1
)

echo Portas liberadas para redes privadas/domesticas:
echo   TCP %PORT_TCP%  ^(mensagens^)
echo   UDP %PORT_UDP%  ^(descoberta automatica^)
echo.
echo As regras aparecem no Firewall do Windows com o nome "Comunicador",
echo e podem ser removidas por la a qualquer momento.
echo.
echo IMPORTANTE: rode este arquivo em TODOS os computadores que usam o
echo Comunicador, e confirme que a rede esta marcada como "Rede particular"
echo no Windows ^(Configuracoes ^> Rede e Internet^).
echo ===============================================
pause
exit /b 0
