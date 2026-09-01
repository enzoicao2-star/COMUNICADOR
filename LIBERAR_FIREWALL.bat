@echo off
setlocal

rem Configura o Windows para o Comunicador funcionar entre computadores:
rem  - marca as redes FISICAS como "Particular" (no perfil Publico o Windows
rem    bloqueia descoberta na rede; Particular tambem e o perfil correto para
rem    compartilhamento de arquivos / unidades de rede continuarem funcionando)
rem  - libera as portas 57931/57932 no Firewall
rem
rem O script SO ADICIONA liberacoes para essas duas portas. Ele nao remove nem
rem bloqueia nenhuma outra regra, e nao mexe em adaptadores virtuais/VPN.
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
echo   Comunicador - configurar rede
echo ===============================================
echo.

echo [1/2] Verificando o perfil das redes...
powershell -NoProfile -Command ^
    "$fisicas = Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up';" ^
    "if (-not $fisicas) { Write-Host '      Nenhuma placa de rede fisica ativa encontrada.'; exit 0 };" ^
    "foreach ($a in $fisicas) {" ^
    "  $perfil = Get-NetConnectionProfile -InterfaceIndex $a.ifIndex -ErrorAction SilentlyContinue;" ^
    "  if (-not $perfil) { continue };" ^
    "  if ($perfil.NetworkCategory -eq 'Public') {" ^
    "    try { Set-NetConnectionProfile -InterfaceIndex $a.ifIndex -NetworkCategory Private -ErrorAction Stop;" ^
    "      Write-Host ('      ALTERADO: ' + $perfil.Name + ' (' + $a.Name + ') Publica -> Particular') }" ^
    "    catch { Write-Host ('      AVISO: nao consegui alterar ' + $perfil.Name) } }" ^
    "  else { Write-Host ('      OK, ja estava correta: ' + $perfil.Name + ' (' + $perfil.NetworkCategory + ')') } }"

echo.
echo [2/2] Liberando as portas no Firewall do Windows...
netsh advfirewall firewall delete rule name="Comunicador" >nul 2>nul
netsh advfirewall firewall delete rule name="Comunicador (descoberta)" >nul 2>nul

rem profile=any cobre Publico/Particular/Dominio — se a rede voltar a ser
rem classificada como Publica, a regra continua valendo.
netsh advfirewall firewall add rule name="Comunicador" dir=in action=allow protocol=TCP localport=%PORT_TCP% profile=any >nul
netsh advfirewall firewall add rule name="Comunicador (descoberta)" dir=in action=allow protocol=UDP localport=%PORT_UDP% profile=any >nul

if errorlevel 1 (
    echo ERRO: nao foi possivel criar as regras no Firewall.
    pause
    exit /b 1
)

echo       Portas liberadas: TCP %PORT_TCP% ^(mensagens^) e UDP %PORT_UDP% ^(descoberta^).
echo.
echo ===============================================
echo   Pronto! Rede configurada.
echo.
echo   Nada mais foi alterado: nenhuma outra regra de Firewall foi
echo   removida, e adaptadores virtuais/VPN nao foram tocados.
echo   Compartilhamento de arquivos e unidades de rede continuam
echo   funcionando normalmente ^(o perfil Particular e justamente o
echo   perfil em que o Windows os permite^).
echo.
echo   Execute este arquivo em TODOS os computadores que usam o
echo   Comunicador, e depois abra o painel novamente.
echo ===============================================
pause
exit /b 0
