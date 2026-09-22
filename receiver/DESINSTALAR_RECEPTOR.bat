@echo off
setlocal enabledelayedexpansion

set "RECEIVER_VERSION=2.5.1"
set "TASK_NAME=Comunicador Receptor"
set "INSTALL_ROOT=%LOCALAPPDATA%\Comunicador\Receptor"
set "COMUNICADOR_RECEPTOR_SCRIPT=%LOCALAPPDATA%\Comunicador\Receptor\app\receptor.py"
set "LOCAL_COPY=%TEMP%\DESINSTALAR_RECEPTOR_Comunicador.bat"

if /I "%~1"=="--verificar" goto :verificar

rem A copia temporaria e essencial: normalmente este BAT fica dentro da pasta
rem que sera removida. Executa-lo dali impediria a exclusao completa ou faria o
rem cmd perder o restante do arquivo no meio da desinstalacao.
if /I not "%~1"=="--executar" (
    if /I not "%~f0"=="%LOCAL_COPY%" (
        copy /y "%~f0" "%LOCAL_COPY%" >nul
        if errorlevel 1 (
            echo ERRO: nao foi possivel preparar a copia temporaria do desinstalador.
            echo       Destino: %LOCAL_COPY%
            pause
            exit /b 1
        )
        start "" "%LOCAL_COPY%" --executar
        exit /b 0
    )
)

cd /d "%TEMP%"

rem Remover a tarefa do Agendador e as regras de Firewall exige administrador.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Esta desinstalacao precisa de permissao de administrador. Uma janela do
    echo Windows vai pedir sua confirmacao ^(UAC^)...
    set "DESINSTALADOR_LOCAL=%~f0"
    powershell -NoProfile -Command ^
        "try { Start-Process -FilePath $env:DESINSTALADOR_LOCAL -ArgumentList '--executar' -Verb RunAs -ErrorAction Stop; exit 0 } catch { Write-Host ('ERRO: permissao nao concedida: ' + $_.Exception.Message); exit 1 }"
    if errorlevel 1 (
        echo A desinstalacao nao foi executada.
        pause
        exit /b 1
    )
    exit /b 0
)

echo ===============================================
echo   Comunicador Receptor %RECEIVER_VERSION% - desinstalacao
echo ===============================================
echo.

echo [1/5] Parando somente o receptor instalado, se estiver rodando...
rem O caminho completo evita encerrar outro script chamado receptor.py.
powershell -NoProfile -Command ^
    "$target=$env:COMUNICADOR_RECEPTOR_SCRIPT;" ^
    "try{$p=Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object { $_.Name -like 'python*' -and $_.CommandLine -like ('*'+$target+'*') };" ^
    "if($p){$p | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }; Write-Host '      Processo encerrado.'}" ^
    "else{Write-Host '      O receptor nao estava rodando.'}}catch{Write-Host ('      AVISO: nao consegui consultar os processos: '+$_.Exception.Message)}"

echo.
echo [2/5] Removendo a inicializacao automatica...
schtasks /query /tn "%TASK_NAME%" >nul 2>nul
if %errorlevel%==0 (
    schtasks /delete /tn "%TASK_NAME%" /f >nul 2>nul
    if errorlevel 1 (
        echo       AVISO: nao foi possivel remover a tarefa "%TASK_NAME%".
    ) else (
        echo       Tarefa "%TASK_NAME%" removida.
    )
) else (
    echo       Nenhuma tarefa "%TASK_NAME%" encontrada.
)
powershell -NoProfile -Command ^
    "$atalho=Join-Path ([Environment]::GetFolderPath('Startup')) 'Comunicador Receptor.lnk';" ^
    "if(Test-Path $atalho){Remove-Item -LiteralPath $atalho -Force; Write-Host '      Atalho da pasta Inicializar removido.'}" ^
    "else{Write-Host '      Nenhum atalho na pasta Inicializar.'}"

echo.
echo [3/5] Removendo somente as regras de Firewall do receptor...
set "REMOVEU=0"
for %%R in ("Comunicador Receptor" "Comunicador Receptor (descoberta)") do (
    netsh advfirewall firewall show rule name=%%R >nul 2>nul
    if not errorlevel 1 (
        netsh advfirewall firewall delete rule name=%%R >nul 2>nul
        if not errorlevel 1 (
            echo       Removida: %%R
            set "REMOVEU=1"
        )
    )
)
if "!REMOVEU!"=="0" echo       Nenhuma regra exclusiva do receptor encontrada.
echo       As regras do painel Comunicador foram preservadas.

echo.
echo [4/5] Verificando as configuracoes compartilhadas de rede...
set "PANEL_PRESENT=0"
if exist "%USERPROFILE%\Documents\P5\release\Comunicador.exe" set "PANEL_PRESENT=1"
if exist "%LOCALAPPDATA%\Comunicador\Painel\Comunicador.exe" set "PANEL_PRESENT=1"
netsh advfirewall firewall show rule name="Comunicador" >nul 2>nul
if not errorlevel 1 set "PANEL_PRESENT=1"

if "!PANEL_PRESENT!"=="1" (
    echo       Um painel Comunicador continua instalado ou configurado.
    echo       O perfil de rede e o backup compartilhado foram preservados.
) else (
    powershell -NoProfile -Command ^
        "$bk = Join-Path $env:LOCALAPPDATA 'Comunicador\backup_rede.csv';" ^
        "if (-not (Test-Path $bk)) { Write-Host '      Nenhum backup de rede encontrado.'; exit 0 };" ^
        "Get-Content $bk | Where-Object { $_ -match ',' } | ForEach-Object {" ^
        "  $p = $_ -split ','; $idx = [int]$p[0]; $cat = $p[1].Trim();" ^
        "  $atual = Get-NetConnectionProfile -InterfaceIndex $idx -ErrorAction SilentlyContinue;" ^
        "  if (-not $atual) { Write-Host ('      Interface ' + $idx + ' nao esta mais ativa, ignorando.'); return };" ^
        "  if ($atual.NetworkCategory -eq $cat) { Write-Host ('      Ja estava no estado original: ' + $atual.Name + ' (' + $cat + ')'); return };" ^
        "  try { Set-NetConnectionProfile -InterfaceIndex $idx -NetworkCategory $cat -ErrorAction Stop;" ^
        "    Write-Host ('      RESTAURADO: ' + $atual.Name + ' -^> ' + $cat) }" ^
        "  catch { Write-Host ('      AVISO: nao consegui restaurar a interface ' + $idx) } };" ^
        "Remove-Item $bk -Force -ErrorAction SilentlyContinue"
)

echo.
echo [5/5] Removendo os arquivos instalados...
if exist "%INSTALL_ROOT%" (
    rmdir /s /q "%INSTALL_ROOT%"
    if exist "%INSTALL_ROOT%" (
        echo       ERRO: alguns arquivos nao puderam ser removidos de:
        echo       %INSTALL_ROOT%
        pause
        exit /b 1
    )
    echo       Pasta removida: %INSTALL_ROOT%
) else (
    echo       Nada para remover em: %INSTALL_ROOT%
)

echo.
echo ===============================================
echo   Comunicador Receptor desinstalado.
echo   Processo, inicializacao, regras e arquivos removidos.
if "!PANEL_PRESENT!"=="1" echo   As configuracoes usadas pelo painel foram preservadas.
echo.
echo   O Python em si NAO foi removido da maquina.
echo ===============================================
echo.
echo Esta janela fecha sozinha em 5 segundos...
ping -n 6 127.0.0.1 >nul 2>nul
exit /b 0

:verificar
echo ===============================================
echo   Verificacao segura do desinstalador %RECEIVER_VERSION%
echo ===============================================
echo Nenhuma alteracao sera feita neste modo.
echo.

echo [1] Pasta que seria removida:
echo     %INSTALL_ROOT%
if exist "%INSTALL_ROOT%" (
    echo     Estado: EXISTE
) else (
    echo     Estado: nao existe
)

echo.
echo [2] Processo exato do receptor instalado:
powershell -NoProfile -Command ^
    "$target=$env:COMUNICADOR_RECEPTOR_SCRIPT;" ^
    "try{$p=Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object { $_.Name -like 'python*' -and $_.CommandLine -like ('*'+$target+'*') };" ^
    "if($p){$p | ForEach-Object { Write-Host ('    RODANDO - PID ' + $_.ProcessId) }}else{Write-Host '    nao esta rodando'}}" ^
    "catch{Write-Host '    consulta indisponivel sem permissao de administrador'}"

echo.
echo [3] Inicializacao automatica:
schtasks /query /tn "%TASK_NAME%" >nul 2>nul
if %errorlevel%==0 (echo     Tarefa agendada: EXISTE) else (echo     Tarefa agendada: nao existe)
powershell -NoProfile -Command ^
    "$atalho=Join-Path ([Environment]::GetFolderPath('Startup')) 'Comunicador Receptor.lnk';" ^
    "if(Test-Path $atalho){Write-Host '    Atalho Inicializar: EXISTE'}else{Write-Host '    Atalho Inicializar: nao existe'}"

echo.
echo [4] Regras exclusivas do receptor:
for %%R in ("Comunicador Receptor" "Comunicador Receptor (descoberta)") do (
    netsh advfirewall firewall show rule name=%%R >nul 2>nul
    if not errorlevel 1 (echo     %%~R: EXISTE) else (echo     %%~R: nao existe)
)

echo.
echo [5] Backup compartilhado de rede:
if exist "%LOCALAPPDATA%\Comunicador\backup_rede.csv" (
    echo     EXISTE - sera preservado se houver um painel configurado
) else (
    echo     nao existe
)

echo.
echo Verificacao concluida. O desinstalador encontrou todos os alvos de forma
echo restrita ao Comunicador Receptor e nao alterou nada neste computador.
exit /b 0
