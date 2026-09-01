@echo off
setlocal enabledelayedexpansion

rem Reune num so lugar tudo que costuma explicar "o painel nao me encontra".
rem Nao altera nada: so le e mostra. Pode rodar sem administrador.

set "BASE=%LOCALAPPDATA%\Comunicador\Receptor"
set "INSTALL_DIR=%BASE%\app"
set "PORT_TCP=57931"
set "PORT_UDP=57932"
set "TASK_NAME=Comunicador Receptor"

echo ===============================================
echo   Comunicador Receptor - diagnostico
echo ===============================================
echo.
echo Computador: %COMPUTERNAME%
echo Usuario:    %USERNAME%
echo Data:       %DATE% %TIME%
echo.

echo [1] PYTHON
where python >nul 2>nul
if %errorlevel%==0 (
    for /f "delims=" %%P in ('where python') do (
        if not defined PY set "PY=%%P"
    )
    echo     Encontrado: !PY!
    "!PY!" --version 2>&1
) else (
    echo     NAO ENCONTRADO no PATH.
)
echo.

echo [2] ARQUIVOS DO RECEPTOR
if exist "%INSTALL_DIR%\receptor.py" (
    echo     receptor.py  OK
) else (
    echo     receptor.py  FALTANDO  ^<-- a instalacao nao completou
)
if exist "%INSTALL_DIR%\protocolo.py" (
    echo     protocolo.py OK
) else (
    echo     protocolo.py FALTANDO  ^<-- receptor nao consegue nem iniciar
)
echo.

echo [3] O RECEPTOR ESTA RODANDO?
powershell -NoProfile -Command ^
    "$p = Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'python*' -and $_.CommandLine -like '*receptor.py*' };" ^
    "if ($p) { $p | ForEach-Object { Write-Host ('    RODANDO - PID ' + $_.ProcessId) } } else { Write-Host '    NAO ESTA RODANDO' }"
echo.

echo [4] ESCUTANDO NA PORTA %PORT_TCP%?
powershell -NoProfile -Command ^
    "try { $c = New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1', %PORT_TCP%); $c.Close();" ^
    "  Write-Host '    SIM - porta %PORT_TCP% respondendo' } catch { Write-Host '    NAO - nada escutando na porta %PORT_TCP%' }"
echo.

echo [5] INICIALIZACAO AUTOMATICA
schtasks /query /tn "%TASK_NAME%" >nul 2>nul
if %errorlevel%==0 (
    echo     Tarefa agendada: CONFIGURADA
) else (
    echo     Tarefa agendada: nao existe
)
powershell -NoProfile -Command ^
    "$l = Join-Path ([Environment]::GetFolderPath('Startup')) 'Comunicador Receptor.lnk';" ^
    "if (Test-Path $l) { Write-Host '    Pasta Inicializar: CONFIGURADA' } else { Write-Host '    Pasta Inicializar: nao existe' }"
echo.

echo [6] REDE E FIREWALL
powershell -NoProfile -Command ^
    "Get-NetConnectionProfile | ForEach-Object { Write-Host ('    Rede: ' + $_.Name + ' = ' + $_.NetworkCategory) }"
netsh advfirewall firewall show rule name="Comunicador Receptor" >nul 2>nul
if %errorlevel%==0 (
    echo     Regra de firewall TCP: EXISTE
) else (
    echo     Regra de firewall TCP: nao existe ^(normal se usar conexao reversa^)
)
echo.

echo [7] PAINEIS VISIVEIS NA REDE ^(porta %PORT_TCP%^)
echo     procurando, aguarde...
powershell -NoProfile -Command ^
    "$meu = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.PrefixLength -eq 24 } | Select-Object -First 1).IPAddress;" ^
    "if (-not $meu) { Write-Host '    nao identifiquei a sub-rede'; exit };" ^
    "$rede = ($meu -split '\.')[0..2] -join '.';" ^
    "Write-Host ('    varrendo ' + $rede + '.1-254 ...');" ^
    "$t = 1..254 | ForEach-Object { $c = New-Object Net.Sockets.TcpClient; [PSCustomObject]@{ IP = \"$rede.$_\"; C = $c; T = $c.ConnectAsync(\"$rede.$_\", %PORT_TCP%) } };" ^
    "Start-Sleep -Seconds 5;" ^
    "$achou = $false;" ^
    "foreach ($x in $t) { if ($x.T.Status -eq 'RanToCompletion') { Write-Host ('    RESPONDEU: ' + $x.IP); $achou = $true }; $x.C.Close() };" ^
    "if (-not $achou) { Write-Host '    nenhum host respondeu - o painel esta aberto na outra maquina?' }"
echo.

echo [8] ULTIMAS LINHAS DO LOG
rem Log vazio conta como "nunca rodou": o arquivo e criado na inicializacao,
rem entao 0 bytes significa que o processo morreu antes de registrar qualquer coisa.
powershell -NoProfile -Command ^
    "$log = '%BASE%\receptor.log';" ^
    "if (-not (Test-Path $log)) {" ^
    "  Write-Host '    receptor.log NAO EXISTE';" ^
    "  Write-Host '    -> o receptor nunca iniciou. Rode INSTALAR_RECEPTOR.bat de novo.' }" ^
    "elseif ((Get-Item $log).Length -eq 0) {" ^
    "  Write-Host '    receptor.log esta VAZIO';" ^
    "  Write-Host '    -> o receptor morreu logo ao iniciar. Rode INSTALAR_RECEPTOR.bat de novo.' }" ^
    "else { Get-Content $log -Tail 15 | ForEach-Object { Write-Host ('    ' + $_) } }"
echo.

echo ===============================================
echo   Fim do diagnostico. Tire um print desta tela
echo   e mande para quem esta ajudando.
echo ===============================================
pause
exit /b 0
