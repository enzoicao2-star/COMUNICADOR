@echo off
setlocal

rem Remover a tarefa do Agendador e as regras de Firewall exige administrador.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Esta desinstalacao precisa de permissao de administrador. Uma janela do
    echo Windows vai pedir sua confirmacao ^(UAC^)...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo ===============================================
echo   Comunicador Receptor - desinstalacao
echo ===============================================
echo.

set "TASK_NAME=Comunicador Receptor"
set "INSTALL_ROOT=%LOCALAPPDATA%\Comunicador\Receptor"

echo [1/5] Parando o receptor, se estiver rodando...
rem Filtra por processos python: sem isso o proprio powershell entra no
rem resultado (a linha de comando dele contem 'receptor.py') e ele se mata
rem antes de terminar a desinstalacao.
powershell -NoProfile -Command ^
    "Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'python*' -and $_.CommandLine -like '*receptor.py*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"

echo.
echo [2/5] Removendo a tarefa do Agendador de Tarefas do Windows...
schtasks /query /tn "%TASK_NAME%" >nul 2>nul
if %errorlevel%==0 (
    schtasks /delete /tn "%TASK_NAME%" /f >nul 2>nul
    echo       Tarefa "%TASK_NAME%" removida.
) else (
    echo       Nenhuma tarefa "%TASK_NAME%" encontrada.
)

echo.
echo [3/5] Removendo as regras de Firewall criadas pelo Comunicador...
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
echo [4/5] Restaurando o perfil original das redes...
powershell -NoProfile -Command ^
    "$bk = Join-Path $env:LOCALAPPDATA 'Comunicador\backup_rede.csv';" ^
    "if (-not (Test-Path $bk)) { Write-Host '      Nenhum backup encontrado — as redes nao foram alteradas nesta maquina.'; exit 0 };" ^
    "Get-Content $bk | Where-Object { $_ -match ',' } | ForEach-Object {" ^
    "  $p = $_ -split ',';" ^
    "  $idx = [int]$p[0]; $cat = $p[1].Trim();" ^
    "  $atual = Get-NetConnectionProfile -InterfaceIndex $idx -ErrorAction SilentlyContinue;" ^
    "  if (-not $atual) { Write-Host ('      Interface ' + $idx + ' nao esta mais ativa, ignorando.'); return };" ^
    "  if ($atual.NetworkCategory -eq $cat) { Write-Host ('      Ja estava no estado original: ' + $atual.Name + ' (' + $cat + ')'); return };" ^
    "  try { Set-NetConnectionProfile -InterfaceIndex $idx -NetworkCategory $cat -ErrorAction Stop;" ^
    "    Write-Host ('      RESTAURADO: ' + $atual.Name + ' -> ' + $cat) }" ^
    "  catch { Write-Host ('      AVISO: nao consegui restaurar a interface ' + $idx) } };" ^
    "Remove-Item $bk -Force -ErrorAction SilentlyContinue"

echo.
echo [5/5] Removendo arquivos instalados...
if exist "%INSTALL_ROOT%" (
    rmdir /s /q "%INSTALL_ROOT%"
    echo       Pasta removida: %INSTALL_ROOT%
) else (
    echo       Nada para remover em: %INSTALL_ROOT%
)

echo.
echo ===============================================
echo   Comunicador Receptor desinstalado e
echo   configuracoes do Windows revertidas.
echo.
echo   O Python em si NAO foi removido da maquina.
echo ===============================================
echo.
echo Esta janela fecha sozinha em 5 segundos...
rem ping em vez de timeout: timeout falha quando a entrada esta redirecionada.
ping -n 6 127.0.0.1 >nul 2>nul
exit /b 0
