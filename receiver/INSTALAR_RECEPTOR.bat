@echo off
setlocal enabledelayedexpansion
set "RECEIVER_VERSION=2.5.9"
if not defined COMUNICADOR_SOURCE_DIR set "COMUNICADOR_SOURCE_DIR=%~dp0"

rem Modo de diagnostico seguro: testa a deteccao sem instalar nem pedir UAC.
if /I "%~1"=="--verificar-python" (
    if "%~2"=="" (call :detectar_python) else (call :validar_python "%~2")
    if defined PYTHON_EXE (
        echo Python encontrado em: !PYTHON_EXE!
        exit /b 0
    )
    echo Python compativel nao encontrado.
    exit /b 1
)
if /I "%~1"=="--verificar-erro-python" (
    set "PYTHON_INSTALL_LOG=%~2"
    set "PYTHON_INSTALL_CODE=%~3"
    call :mostrar_erro_python
    exit /b 0
)

rem Python e notificacoes devem rodar na conta que fez login. Elevar o BAT
rem inteiro pode trocar para outra conta administradora, esconder um Python ja
rem instalado e fazer o receptor abrir na sessao de usuario errada.
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
    echo Instalando na conta atual, sem trocar de usuario pelo UAC.
    echo Se o Firewall exigir administrador, a conexao reversa continuara ativa.
    echo.
    set "SEM_ADMIN=1"
)

echo ===============================================
echo   Comunicador Receptor %RECEIVER_VERSION% - instalacao
echo ===============================================
echo.

set "REPO_RAW=https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/receiver"
set "INSTALL_ROOT=%LOCALAPPDATA%\Comunicador\Receptor"
set "INSTALL_DIR=%INSTALL_ROOT%\app"
set "DOWNLOAD_DIR=%TEMP%\Comunicador-Receptor-%RECEIVER_VERSION%-%RANDOM%%RANDOM%"
set "CACHE_BUSTER=%RANDOM%%RANDOM%%RANDOM%"
set "COMUNICADOR_RECEPTOR_SCRIPT=%LOCALAPPDATA%\Comunicador\Receptor\app\receptor.py"
set "TASK_NAME=Comunicador Receptor"
set "PYTHON_INSTALLER_URL=https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.exe"
set "PYTHON_INSTALLER=%TEMP%\Comunicador-Python-3.12.10-%RANDOM%.exe"
set "PYTHON_INSTALL_LOG=%TEMP%\Comunicador-Python-install-%RANDOM%.log"
set "PYTHON_EXE="
set "PORT_TCP=57931"
set "PORT_UDP=57932"

echo [1/8] Verificando se o Python ja esta instalado...
call :detectar_python

if defined PYTHON_EXE (
    echo       Python encontrado em: !PYTHON_EXE!
) else (
    echo       Python nao encontrado nesta maquina.
    echo.
    echo [2/8] Baixando o instalador oficial do Python ^(python.org^)...
    curl.exe --fail --location --silent --show-error --retry 2 -o "%PYTHON_INSTALLER%" "%PYTHON_INSTALLER_URL%"
    if errorlevel 1 (
        echo ERRO: falha ao baixar o instalador do Python. Verifique sua conexao com a internet.
        goto :erro
    )

    rem Instalar para a conta que usa o receptor evita conflito com outra
    rem instalacao global e nao exige modificar o PATH do computador.
    echo       Instalando Python para este usuario...
    "%PYTHON_INSTALLER%" /quiet InstallAllUsers=0 PrependPath=0 ^
        Include_launcher=0 Include_test=0 Include_pip=1 Include_tcltk=1 ^
        /log "!PYTHON_INSTALL_LOG!"
    set "PYTHON_INSTALL_CODE=!errorlevel!"
    if "!PYTHON_INSTALL_CODE!"=="3010" echo       O instalador pediu reinicio; verificando se o Python ja funciona.
    if not "!PYTHON_INSTALL_CODE!"=="0" if not "!PYTHON_INSTALL_CODE!"=="3010" (
        echo ERRO: instalador do Python retornou o codigo !PYTHON_INSTALL_CODE!.
        call :mostrar_erro_python
        goto :erro
    )
    del "!PYTHON_INSTALLER!" >nul 2>nul

    echo       Verificando instalacao...
    set "PYTHON_EXE="
    call :detectar_python
    if not defined PYTHON_EXE (
        echo ERRO: Python foi instalado mas nao passou na verificacao de versao, Tk ou pythonw.
        call :mostrar_erro_python
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
echo [3/8] Preparando a atualizacao segura...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
mkdir "%DOWNLOAD_DIR%"
if errorlevel 1 (
    echo ERRO: nao foi possivel criar a pasta temporaria de download.
    goto :erro
)

echo.
echo [4/8] Baixando e validando o receptor %RECEIVER_VERSION%...
curl.exe --help all 2>nul | findstr /L /C:"--parallel" >nul
if errorlevel 1 (
    rem Versoes antigas do curl nao suportam --parallel; usa o fluxo anterior.
    echo       Download paralelo indisponivel; tentando arquivo por arquivo...
    curl.exe -fsSL -o "%DOWNLOAD_DIR%\receptor.py" "%REPO_RAW%/receptor.py?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!"
    if errorlevel 1 goto :erro_download
    curl.exe -fsSL -o "%DOWNLOAD_DIR%\protocolo.py" "%REPO_RAW%/protocolo.py?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!"
    if errorlevel 1 goto :erro_download
    curl.exe -fsSL -o "%DOWNLOAD_DIR%\requirements.txt" "%REPO_RAW%/requirements.txt?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!"
    if errorlevel 1 goto :erro_download
    curl.exe -fsSL -o "%DOWNLOAD_DIR%\DIAGNOSTICO.bat" "%REPO_RAW%/DIAGNOSTICO.bat?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!"
    if errorlevel 1 goto :erro_download
    curl.exe -fsSL -o "%DOWNLOAD_DIR%\DESINSTALAR_RECEPTOR.bat" "%REPO_RAW%/DESINSTALAR_RECEPTOR.bat?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!"
    if errorlevel 1 goto :erro_download
) else (
    curl.exe --parallel --parallel-immediate --fail-early -fsSL ^
        -o "%DOWNLOAD_DIR%\receptor.py" "%REPO_RAW%/receptor.py?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!" ^
        -o "%DOWNLOAD_DIR%\protocolo.py" "%REPO_RAW%/protocolo.py?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!" ^
        -o "%DOWNLOAD_DIR%\requirements.txt" "%REPO_RAW%/requirements.txt?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!" ^
        -o "%DOWNLOAD_DIR%\DIAGNOSTICO.bat" "%REPO_RAW%/DIAGNOSTICO.bat?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!" ^
        -o "%DOWNLOAD_DIR%\DESINSTALAR_RECEPTOR.bat" "%REPO_RAW%/DESINSTALAR_RECEPTOR.bat?v=%RECEIVER_VERSION%&t=!CACHE_BUSTER!"
    if errorlevel 1 goto :erro_download
)
for %%F in (receptor.py protocolo.py requirements.txt DIAGNOSTICO.bat DESINSTALAR_RECEPTOR.bat) do (
    if not exist "%DOWNLOAD_DIR%\%%F" goto :erro_download
    for %%Z in ("%DOWNLOAD_DIR%\%%F") do if %%~zZ LEQ 0 goto :erro_download
)

"!PYTHON_EXE!" -m py_compile "%DOWNLOAD_DIR%\protocolo.py" "%DOWNLOAD_DIR%\receptor.py"
if errorlevel 1 (
    echo ERRO: os arquivos baixados nao passaram na validacao do Python.
    goto :erro_download
)
findstr /L /C:"RECEIVER_VERSION = " "%DOWNLOAD_DIR%\receptor.py" | findstr /L /C:"%RECEIVER_VERSION%" >nul
if errorlevel 1 (
    echo ERRO: o GitHub ainda nao entregou a versao %RECEIVER_VERSION% esperada.
    echo        Aguarde alguns segundos e execute o instalador novamente.
    goto :erro_download
)
findstr /L /C:"--verificar" "%DOWNLOAD_DIR%\DESINSTALAR_RECEPTOR.bat" >nul
if errorlevel 1 (
    echo ERRO: o GitHub entregou uma copia antiga do desinstalador.
    echo        Execute o instalador novamente para baixar a revisao segura.
    goto :erro_download
)
findstr /L /C:"As regras do painel Comunicador foram preservadas." "%DOWNLOAD_DIR%\DESINSTALAR_RECEPTOR.bat" >nul
if errorlevel 1 (
    echo ERRO: a validacao de seguranca do desinstalador falhou.
    goto :erro_download
)

rem So encerramos a versao anterior depois de baixar e validar a nova.
powershell -NoProfile -Command ^
    "$target=$env:COMUNICADOR_RECEPTOR_SCRIPT;" ^
    "$p=Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'python*' -and $_.CommandLine -like ('*'+$target+'*') };" ^
    "if($p){$p | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue };" ^
    "Write-Host '      Receptor anterior encerrado; instalando a nova versao.'}"

copy /y "%DOWNLOAD_DIR%\receptor.py" "%INSTALL_DIR%\receptor.py" >nul
if errorlevel 1 goto :erro_copia
copy /y "%DOWNLOAD_DIR%\protocolo.py" "%INSTALL_DIR%\protocolo.py" >nul
if errorlevel 1 goto :erro_copia
copy /y "%DOWNLOAD_DIR%\requirements.txt" "%INSTALL_DIR%\requirements.txt" >nul
if errorlevel 1 goto :erro_copia
copy /y "%DOWNLOAD_DIR%\DIAGNOSTICO.bat" "%INSTALL_ROOT%\DIAGNOSTICO.bat" >nul
if errorlevel 1 goto :erro_copia
copy /y "%DOWNLOAD_DIR%\DESINSTALAR_RECEPTOR.bat" "%INSTALL_ROOT%\DESINSTALAR_RECEPTOR.bat" >nul
if errorlevel 1 goto :erro_copia
rmdir /s /q "%DOWNLOAD_DIR%" >nul 2>nul
goto :download_ok

:erro_download
echo ERRO: falha ao baixar os arquivos do receptor. Verifique sua conexao com a internet.
goto :erro

:erro_copia
echo ERRO: falha ao substituir os arquivos do receptor.
goto :erro

:download_ok
echo       Receptor %RECEIVER_VERSION% validado e salvo em: %INSTALL_DIR%

echo.
echo [5/8] Instalando dependencias Python ^(pystray, Pillow^)...
"!PYTHON_EXE!" -c "from importlib.metadata import version; from pip._vendor.packaging.requirements import Requirement; from pathlib import Path; import sys; reqs=[Requirement(line) for line in Path(sys.argv[1]).read_text(encoding='utf-8').splitlines() if line.strip() and not line.lstrip().startswith('#')]; import pystray, PIL; sys.exit(0 if all(r.specifier.contains(version(r.name)) for r in reqs) else 1)" "%INSTALL_DIR%\requirements.txt" >nul 2>nul
if not errorlevel 1 (
    echo       Dependencias ja instaladas; nenhuma espera pelo pip.
) else (
    "!PYTHON_EXE!" -m pip install --user --disable-pip-version-check --quiet -r "%INSTALL_DIR%\requirements.txt"
    if errorlevel 1 (
        echo AVISO: nao foi possivel instalar todas as dependencias opcionais.
        echo        O receptor funciona normalmente, so o icone na bandeja fica desativado.
    )
)

echo.
echo [6/8] Configurando inicializacao automatica e Firewall...
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

echo       Verificando e preservando as configuracoes de rede...
echo       Marcando as redes fisicas como "Particular" ^(no perfil Publico o
echo       Windows bloqueia a descoberta entre computadores; adaptadores
echo       virtuais/VPN/loopback nao sao tocados^)...
powershell -NoProfile -Command ^
    "$bk = Join-Path $env:LOCALAPPDATA 'Comunicador\backup_rede.csv';" ^
    "$adapters = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Loopback' });" ^
    "$indices = @($adapters | ForEach-Object { $_.ifIndex });" ^
    "$profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object { $indices -contains $_.InterfaceIndex });" ^
    "if (Test-Path $bk) { Write-Host '      Backup anterior preservado (estado original ja guardado).' } else {" ^
    "  New-Item -ItemType Directory -Force -Path (Split-Path $bk) | Out-Null;" ^
    "  $profiles | ForEach-Object { '{0},{1}' -f $_.InterfaceIndex,$_.NetworkCategory } | Set-Content -Path $bk -Encoding UTF8;" ^
    "  Write-Host ('      Backup salvo em: ' + $bk) };" ^
    "$profiles | Where-Object { $_.NetworkCategory -eq 'Public' } | ForEach-Object { try { Set-NetConnectionProfile -InterfaceIndex $_.InterfaceIndex -NetworkCategory Private -ErrorAction Stop; Write-Host ('      ALTERADO: ' + $_.Name + ' Publica -> Particular') } catch {} }"

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
echo [7/8] Iniciando o receptor %RECEIVER_VERSION% agora...
echo.
echo [8/8] Confirmando processo, versao e inicializacao automatica...
set "RECEPTOR_OK="
rem Inicia direto pelo pythonw, sem depender do Agendador. O PID retornado
rem identifica exatamente esta instancia e evita a espera fixa de varios segundos.
powershell -NoProfile -NonInteractive -Command ^
    "$target=$env:COMUNICADOR_RECEPTOR_SCRIPT;" ^
    "$argument=[char]34 + $target + [char]34;" ^
    "try { $p=Start-Process -FilePath $env:PYTHONW_EXE -ArgumentList $argument -WorkingDirectory $env:INSTALL_DIR -WindowStyle Hidden -PassThru -ErrorAction Stop } catch { exit 1 };" ^
    "Start-Sleep -Milliseconds 2000; $p.Refresh(); if ($p.HasExited) { exit 1 } else { exit 0 }"
if errorlevel 1 (
    echo       ERRO: o receptor encerrou logo depois de iniciar.
    echo       Verifique o log em: %LOCALAPPDATA%\Comunicador\Receptor\receptor.log
    goto :erro
)
set "RECEPTOR_OK=1"
echo       Processo do receptor %RECEIVER_VERSION% confirmado.
if not defined AUTOSTART_OK (
    echo       ERRO: a inicializacao automatica nao foi configurada.
    goto :erro
)

echo.
echo ===============================================
if defined RECEPTOR_OK (
    echo   Instalacao concluida com sucesso!
    echo   O Comunicador Receptor %RECEIVER_VERSION% esta rodando em segundo plano.
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
echo   Diagnostico e desinstalacao ficam em:
echo   %INSTALL_ROOT%
echo ===============================================
echo.
rem Deu tudo certo: fecha sozinho. So em caso de erro a janela fica
rem aberta, para a mensagem poder ser lida.
echo Esta janela fecha sozinha em 2 segundos...
rem ping em vez de timeout: timeout falha quando a entrada esta redirecionada.
ping -n 3 127.0.0.1 >nul 2>nul
rem Apaga o BAT que acabou de ser executado somente apos o sucesso confirmado.
rem O atualizador nao o restaura enquanto o receptor continuar instalado.
call :agendar_autoexclusao
exit /b 0

:agendar_autoexclusao
rem Instalador compartilhado da conferencia deve continuar disponivel em Z:.
if /I "%COMUNICADOR_SOURCE_DIR%"=="Z:\CONTROLES\conferencia\9\" exit /b 0
set "COMUNICADOR_SELF_DELETE=%~f0"
powershell -NoProfile -NonInteractive -Command "$cleanup='Start-Sleep -Seconds 2; Remove-Item -LiteralPath $env:COMUNICADOR_SELF_DELETE -Force -ErrorAction SilentlyContinue'; $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cleanup)); Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-NonInteractive','-WindowStyle','Hidden','-EncodedCommand',$encoded) -WindowStyle Hidden"
exit /b 0

:validar_python
if defined PYTHON_EXE exit /b 0
set "CANDIDATO_PY=%~1"
if not exist "!CANDIDATO_PY!" exit /b 0
echo(!CANDIDATO_PY!| findstr /I /C:"\WindowsApps\python.exe" >nul
if not errorlevel 1 exit /b 0
rem Evita o for /f com executavel entre aspas: cmd.exe perde as aspas de
rem caminhos com espacos e classificava um Python valido como inexistente.
"!CANDIDATO_PY!" -c "import sys, tkinter; sys.exit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
if errorlevel 1 exit /b 0
for %%Q in ("!CANDIDATO_PY!") do if exist "%%~dpQpythonw.exe" set "PYTHON_EXE=%%~fQ"
exit /b 0

:detectar_python
for /f "delims=" %%P in ('where python 2^>nul') do call :validar_python "%%P"
if not defined PYTHON_EXE (
    for /f "delims=" %%P in ('py -3 -c "import sys; print(sys.executable)" 2^>nul') do call :validar_python "%%P"
)
if not defined PYTHON_EXE (
    for /d %%D in ("%ProgramFiles%\Python3*") do call :validar_python "%%D\python.exe"
)
if not defined PYTHON_EXE (
    for /d %%D in ("%LOCALAPPDATA%\Programs\Python\Python3*") do call :validar_python "%%D\python.exe"
)
exit /b 0

:mostrar_erro_python
echo.
echo ===== ERRO ORIGINAL DO INSTALADOR PYTHON - COPIE DAQUI =====
powershell -NoProfile -NonInteractive -Command ^
    "$prefix=[IO.Path]::GetFileNameWithoutExtension($env:PYTHON_INSTALL_LOG);" ^
    "$files=@(Get-ChildItem -LiteralPath $env:TEMP -Filter ($prefix+'*.log') -File -ErrorAction SilentlyContinue);" ^
    "Write-Output ('Codigo de saida: '+$env:PYTHON_INSTALL_CODE);" ^
    "if (-not $files) { Write-Output 'O instalador nao gerou detalhes em texto.'; exit };" ^
    "$shown=$false;" ^
    "foreach ($file in $files) {" ^
    "  $errors=@(Get-Content -LiteralPath $file.FullName -ErrorAction SilentlyContinue |" ^
    "    Select-String -Pattern 'error|failed|failure|exception|0x[0-9a-f]{8}' |" ^
    "    Select-Object -Last 30 | ForEach-Object { $_.Line });" ^
    "  if ($errors.Count) { $shown=$true; Write-Output ('--- '+$file.Name+' ---'); $errors | ForEach-Object { Write-Output $_ } }" ^
    "};" ^
    "if (-not $shown) { Get-Content -LiteralPath $files[0].FullName -Tail 30 }"
echo ===== FIM DO ERRO ORIGINAL =====
echo.
exit /b 0

:erro
if exist "%DOWNLOAD_DIR%" rmdir /s /q "%DOWNLOAD_DIR%" >nul 2>nul
if defined PYTHON_INSTALLER if exist "!PYTHON_INSTALLER!" del /q "!PYTHON_INSTALLER!" >nul 2>nul
echo.
echo ===============================================
echo   Instalacao FALHOU. Veja os erros acima.
echo ===============================================
pause
exit /b 1
