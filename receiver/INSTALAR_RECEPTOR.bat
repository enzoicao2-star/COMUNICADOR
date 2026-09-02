@echo off
setlocal enabledelayedexpansion

rem Criar a tarefa no Agendador e liberar portas no Firewall exige administrador.
rem Se este .bat nao estiver rodando elevado, pede UAC uma unica vez e continua
rem na janela elevada (a original so passa a bola e fecha).
set "SEM_ADMIN="

rem Copia este .bat para o disco local antes de qualquer coisa.
rem Motivo: rodando de uma unidade de rede mapeada (Z:) ou de um caminho UNC
rem (\\servidor\pasta), a sessao ELEVADA nao enxerga esse caminho — ela e outra
rem sessao de logon e nao herda os drives mapeados. A janela elevada abriria,
rem nao acharia o arquivo e fecharia sem dizer nada.
set "ORIGEM=%~f0"
set "COPIA_LOCAL=%TEMP%\INSTALAR_RECEPTOR_Comunicador.bat"
if /I not "%~d0"=="%SystemDrive%" (
    if /I not "%ORIGEM:~0,2%"=="%SystemDrive%" (
        echo Executando de uma unidade de rede ^(%~d0^). Copiando para o disco
        echo local, porque o modo administrador nao enxerga unidades mapeadas...
        copy /y "%~f0" "%COPIA_LOCAL%" >nul
        if errorlevel 1 (
            echo ERRO: nao foi possivel copiar o instalador para "%COPIA_LOCAL%".
            goto :erro
        )
        echo.
        start "" "%COPIA_LOCAL%"
        exit /b 0
    )
)

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Para configurar o Agendador de Tarefas e o Firewall, o Windows pede
    echo permissao de administrador. Uma janela vai aparecer ^(UAC^)...
    echo.
    powershell -NoProfile -Command "try { Start-Process -FilePath '%~f0' -Verb RunAs -ErrorAction Stop; exit 0 } catch { exit 1 }"
    if not errorlevel 1 exit /b
    rem UAC recusado/indisponivel: seguimos assim mesmo. O receptor ainda e
    rem instalado e iniciado; a inicializacao automatica usa a pasta Inicializar
    rem (que nao precisa de admin) e o Firewall fica por conta do usuario.
    echo AVISO: permissao de administrador nao concedida.
    echo        A instalacao CONTINUA em modo limitado.
    echo.
    set "SEM_ADMIN=1"
)

echo ===============================================
echo   Comunicador Receptor - instalacao
echo ===============================================
echo.

set "REPO_RAW=https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/receiver"
set "INSTALL_DIR=%LOCALAPPDATA%\Comunicador\Receptor\app"
set "TASK_NAME=Comunicador Receptor"
set "PYTHON_INSTALLER_URL=https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.exe"
set "PYTHON_INSTALLER=%TEMP%\comunicador_python_installer.exe"
set "PYTHON_EXE="
set "PORT_TCP=57931"
set "PORT_UDP=57932"

echo [1/7] Verificando se o Python ja esta instalado...
where python >nul 2>nul
if %errorlevel%==0 (
    python -c "print(1)" >"%TEMP%\comunicador_pycheck.txt" 2>nul
    set /p PYCHECK=<"%TEMP%\comunicador_pycheck.txt"
    if "!PYCHECK!"=="1" (
        for /f "delims=" %%P in ('where python') do (
            if not defined PYTHON_EXE set "PYTHON_EXE=%%P"
        )
    )
)

if defined PYTHON_EXE (
    echo       Python encontrado em: !PYTHON_EXE!
) else (
    echo       Python nao encontrado nesta maquina.
    echo.
    echo [2/7] Baixando o instalador oficial do Python ^(python.org^)...
    curl -fsSL -o "%PYTHON_INSTALLER%" "%PYTHON_INSTALLER_URL%"
    if errorlevel 1 (
        echo ERRO: falha ao baixar o instalador do Python. Verifique sua conexao com a internet.
        goto :erro
    )

    echo       Instalando Python silenciosamente para todos os usuarios...
    "%PYTHON_INSTALLER%" /quiet InstallAllUsers=1 PrependPath=1 Include_launcher=0 Include_test=0
    if errorlevel 1 (
        echo ERRO: a instalacao do Python falhou.
        goto :erro
    )
    del "%PYTHON_INSTALLER%" >nul 2>nul

    echo       Verificando instalacao...
    set "PYTHON_EXE="
    for /d %%D in ("%ProgramFiles%\Python3*") do (
        if exist "%%D\python.exe" set "PYTHON_EXE=%%D\python.exe"
    )
    if not defined PYTHON_EXE (
        for /d %%D in ("%LOCALAPPDATA%\Programs\Python\Python3*") do (
            if exist "%%D\python.exe" set "PYTHON_EXE=%%D\python.exe"
        )
    )
    if not defined PYTHON_EXE (
        echo ERRO: nao foi possivel localizar o Python apos a instalacao.
        goto :erro
    )
    echo       Python instalado em: !PYTHON_EXE!
)

for %%F in ("!PYTHON_EXE!") do set "PYTHON_DIR=%%~dpF"
set "PYTHONW_EXE=!PYTHON_DIR!pythonw.exe"
if not exist "!PYTHONW_EXE!" (
    echo ERRO: pythonw.exe nao encontrado em "!PYTHON_DIR!".
    goto :erro
)

echo.
echo [3/7] Preparando pasta de instalacao...
rem Reinstalar por cima e normal: paramos um receptor antigo que ainda esteja
rem rodando, senao ele segura a porta e o novo nao consegue subir.
rem Filtra por processos python: sem isso o proprio powershell entra no
rem resultado (a linha de comando dele contem 'receptor.py') e ele se mata.
powershell -NoProfile -Command ^
    "$p = Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'python*' -and $_.CommandLine -like '*receptor.py*' };" ^
    "if ($p) { $p | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue };" ^
    "  Write-Host '      Receptor anterior encerrado (a instalacao vai substitui-lo).' }"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

echo.
echo [4/7] Baixando receptor.py, protocolo.py e requirements.txt...
curl -fsSL -o "%INSTALL_DIR%\receptor.py" "%REPO_RAW%/receptor.py"
if errorlevel 1 goto :erro_download
curl -fsSL -o "%INSTALL_DIR%\protocolo.py" "%REPO_RAW%/protocolo.py"
if errorlevel 1 goto :erro_download
curl -fsSL -o "%INSTALL_DIR%\requirements.txt" "%REPO_RAW%/requirements.txt"
if errorlevel 1 goto :erro_download
goto :download_ok

:erro_download
echo ERRO: falha ao baixar os arquivos do receptor. Verifique sua conexao com a internet.
goto :erro

:download_ok
echo       Arquivos salvos em: %INSTALL_DIR%

echo.
echo [5/7] Instalando dependencias Python ^(pystray, Pillow^)...
"!PYTHON_EXE!" -m pip install --user --quiet --upgrade pip
"!PYTHON_EXE!" -m pip install --user --quiet -r "%INSTALL_DIR%\requirements.txt"
if errorlevel 1 (
    echo AVISO: nao foi possivel instalar todas as dependencias opcionais.
    echo        O receptor funciona normalmente, so o icone na bandeja fica desativado.
)

echo.
echo [6/7] Configurando inicializacao automatica e Firewall...
schtasks /query /tn "%TASK_NAME%" >nul 2>nul
if %errorlevel%==0 (
    echo       Tarefa existente encontrada, atualizando...
    schtasks /delete /tn "%TASK_NAME%" /f >nul 2>nul
)

set "AUTOSTART_OK="
schtasks /create /tn "%TASK_NAME%" /sc onlogon /rl limited ^
    /tr "\"!PYTHONW_EXE!\" \"%INSTALL_DIR%\receptor.py\"" /f >nul 2>nul
if not errorlevel 1 (
    set "AUTOSTART_OK=tarefa"
    echo       Tarefa "%TASK_NAME%" criada — visivel no Agendador de Tarefas do
    echo       Windows, inicia no login, sem janela de console.
) else (
    rem Sem admin o schtasks devolve "Acesso negado". Em vez de abortar a
    rem instalacao inteira, caimos para a pasta Inicializar, que funciona
    rem com permissao de usuario comum e tambem e visivel/removivel.
    echo       Agendador de Tarefas indisponivel ^(sem permissao^).
    echo       Usando a pasta Inicializar do Windows, que nao exige admin...
    powershell -NoProfile -Command ^
        "$s=(New-Object -ComObject WScript.Shell);" ^
        "$lnk=$s.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Startup')) 'Comunicador Receptor.lnk'));" ^
        "$lnk.TargetPath='!PYTHONW_EXE!';" ^
        "$lnk.Arguments='\"%INSTALL_DIR%\receptor.py\"';" ^
        "$lnk.WorkingDirectory='%INSTALL_DIR%';" ^
        "$lnk.Description='Comunicador Receptor';" ^
        "$lnk.Save()"
    if not errorlevel 1 (
        set "AUTOSTART_OK=inicializar"
        echo       Atalho criado na pasta Inicializar ^(shell:startup^).
    ) else (
        echo       AVISO: nao foi possivel configurar a inicializacao automatica.
        echo       O receptor sera iniciado agora, mas nao apos reiniciar o PC.
    )
)

echo       Salvando as configuracoes atuais de rede antes de alterar...
powershell -NoProfile -Command ^
    "$bk = Join-Path $env:LOCALAPPDATA 'Comunicador\backup_rede.csv';" ^
    "if (Test-Path $bk) { Write-Host '      Backup anterior preservado (estado original ja guardado).' } else {" ^
    "  New-Item -ItemType Directory -Force -Path (Split-Path $bk) | Out-Null;" ^
    "  Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Loopback' } | ForEach-Object {" ^
    "    $p = Get-NetConnectionProfile -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue;" ^
    "    if ($p) { \"$($_.ifIndex),$($p.NetworkCategory)\" } } | Set-Content -Path $bk -Encoding UTF8;" ^
    "  Write-Host ('      Backup salvo em: ' + $bk) }"

echo       Marcando as redes fisicas como "Particular" ^(no perfil Publico o
echo       Windows bloqueia a descoberta entre computadores; adaptadores
echo       virtuais/VPN/loopback nao sao tocados^)...
powershell -NoProfile -Command ^
    "Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Loopback' } | ForEach-Object { $p = Get-NetConnectionProfile -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue; if ($p -and $p.NetworkCategory -eq 'Public') { try { Set-NetConnectionProfile -InterfaceIndex $_.ifIndex -NetworkCategory Private -ErrorAction Stop; Write-Host ('      ALTERADO: ' + $p.Name + ' Publica -> Particular') } catch {} } }"

netsh advfirewall firewall delete rule name="Comunicador Receptor" >nul 2>nul
netsh advfirewall firewall delete rule name="Comunicador Receptor (descoberta)" >nul 2>nul
rem profile=any cobre Publico/Particular/Dominio — se a rede voltar a ser
rem classificada como Publica, a regra continua valendo.
netsh advfirewall firewall add rule name="Comunicador Receptor" dir=in action=allow protocol=TCP localport=%PORT_TCP% profile=any >nul
netsh advfirewall firewall add rule name="Comunicador Receptor (descoberta)" dir=in action=allow protocol=UDP localport=%PORT_UDP% profile=any >nul
if errorlevel 1 (
    echo       AVISO: nao foi possivel liberar as portas no Firewall automaticamente.
    echo       Outros paineis podem nao conseguir encontrar este receptor pela rede.
) else (
    echo       Portas TCP %PORT_TCP% e UDP %PORT_UDP% liberadas no Firewall do Windows.
)

echo.
echo [7/7] Iniciando o receptor agora...
rem Inicia direto pelo pythonw, sem depender do agendador — assim o receptor
rem sobe mesmo que a tarefa nao tenha podido ser criada.
start "" "!PYTHONW_EXE!" "%INSTALL_DIR%\receptor.py"

rem Confirma que o receptor realmente ficou escutando na porta.
ping -n 4 127.0.0.1 >nul 2>nul
set "RECEPTOR_OK="
powershell -NoProfile -Command ^
    "try { $c = New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1', %PORT_TCP%); $c.Close(); exit 0 } catch { exit 1 }"
if not errorlevel 1 (
    set "RECEPTOR_OK=1"
    echo       Receptor confirmado: escutando na porta %PORT_TCP%.
) else (
    echo       AVISO: o receptor nao respondeu na porta %PORT_TCP%.
    echo       Verifique o log em: %LOCALAPPDATA%\Comunicador\Receptor\receptor.log
)

echo.
echo ===============================================
if defined RECEPTOR_OK (
    echo   Instalacao concluida com sucesso!
    echo   O Comunicador Receptor esta rodando em segundo plano.
) else (
    echo   Instalacao concluida com AVISOS - veja acima.
)
if "%AUTOSTART_OK%"=="tarefa"      echo   Inicia sozinho a cada login ^(Agendador de Tarefas^).
if "%AUTOSTART_OK%"=="inicializar" echo   Inicia sozinho a cada login ^(pasta Inicializar^).
if not defined AUTOSTART_OK        echo   ATENCAO: NAO vai iniciar sozinho apos reiniciar o PC.
echo.
echo   Este receptor procura o painel na rede e abre a conexao ele mesmo,
echo   entao aparece no painel sozinho, sem precisar de porta liberada aqui.
if defined SEM_ADMIN (
    echo.
    echo   Rodou SEM administrador: o Firewall nao foi liberado nesta maquina.
    echo   Isso costuma nao ser problema, porque quem abre a conexao e este
    echo   receptor. Se ainda assim o painel nao encontrar este PC, rode o
    echo   instalador de novo e aceite o UAC.
)
echo.
echo   Para desinstalar, execute DESINSTALAR_RECEPTOR.bat
echo ===============================================
echo.
rem Deu tudo certo: fecha sozinho. So em caso de erro a janela fica
rem aberta, para a mensagem poder ser lida.
echo Esta janela fecha sozinha em 5 segundos...
rem ping em vez de timeout: timeout falha quando a entrada esta redirecionada.
ping -n 6 127.0.0.1 >nul 2>nul
exit /b 0

:erro
echo.
echo ===============================================
echo   Instalacao FALHOU. Veja os erros acima.
echo ===============================================
pause
exit /b 1
