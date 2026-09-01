@echo off
setlocal

rem Desfaz TODAS as alteracoes que o Comunicador fez no Windows:
rem  - remove as regras de Firewall criadas por ele (e somente essas)
rem  - restaura o perfil de rede (Publica/Particular) exatamente como estava,
rem    a partir do backup salvo antes da primeira alteracao
rem
rem Nao remove o Comunicador nem o receptor — so devolve as configuracoes do
rem Windows ao estado anterior. Para remover o receptor use
rem DESINSTALAR_RECEPTOR.bat (que ja chama esta reversao).
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Esta reversao precisa de permissao de administrador. Uma janela do
    echo Windows vai pedir sua confirmacao ^(UAC^)...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo ===============================================
echo   Comunicador - reverter configuracoes
echo ===============================================
echo.

echo [1/2] Removendo as regras de Firewall criadas pelo Comunicador...
set "REMOVEU=0"
for %%R in ("Comunicador" "Comunicador (descoberta)" "Comunicador Receptor" "Comunicador Receptor (descoberta)") do (
    netsh advfirewall firewall show rule name=%%R >nul 2>nul
    if not errorlevel 1 (
        netsh advfirewall firewall delete rule name=%%R >nul 2>nul
        echo       Removida: %%R
        set "REMOVEU=1"
    )
)
if "%REMOVEU%"=="0" echo       Nenhuma regra do Comunicador encontrada.
echo       Nenhuma outra regra de Firewall foi tocada.

echo.
echo [2/2] Restaurando o perfil original das redes...
powershell -NoProfile -Command ^
    "$bk = Join-Path $env:LOCALAPPDATA 'Comunicador\backup_rede.csv';" ^
    "if (-not (Test-Path $bk)) { Write-Host '      Nenhum backup encontrado — as redes nao foram alteradas pelo Comunicador nesta maquina.'; exit 0 };" ^
    "Get-Content $bk | Where-Object { $_ -match ',' } | ForEach-Object {" ^
    "  $p = $_ -split ',';" ^
    "  $idx = [int]$p[0]; $cat = $p[1].Trim();" ^
    "  $atual = Get-NetConnectionProfile -InterfaceIndex $idx -ErrorAction SilentlyContinue;" ^
    "  if (-not $atual) { Write-Host ('      Interface ' + $idx + ' nao esta mais ativa, ignorando.'); return };" ^
    "  if ($atual.NetworkCategory -eq $cat) { Write-Host ('      Ja estava no estado original: ' + $atual.Name + ' (' + $cat + ')'); return };" ^
    "  try { Set-NetConnectionProfile -InterfaceIndex $idx -NetworkCategory $cat -ErrorAction Stop;" ^
    "    Write-Host ('      RESTAURADO: ' + $atual.Name + ' -> ' + $cat) }" ^
    "  catch { Write-Host ('      AVISO: nao consegui restaurar a interface ' + $idx) } };" ^
    "Remove-Item $bk -Force -ErrorAction SilentlyContinue;" ^
    "Write-Host '      Backup consumido e removido.'"

echo.
echo ===============================================
echo   Configuracoes revertidas.
echo   O Windows voltou ao estado anterior a instalacao.
echo ===============================================
pause
exit /b 0
