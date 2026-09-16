@echo off
setlocal

set "ROOT=%~dp0"
set "COMUNICADOR_ROOT=%~dp0"
set "VERSAO_ESPERADA=2.2.0.0"
cd /d "%ROOT%"

set "PRECISA_COMPILAR=0"
if not exist "dist\Comunicador.exe" set "PRECISA_COMPILAR=1"

if exist "dist\Comunicador.exe" (
    for /f %%I in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "$r=$env:COMUNICADOR_ROOT; $exe=Join-Path $r 'dist\Comunicador.exe'; $pastas=@((Join-Path $r 'src\Comunicador'),(Join-Path $r 'receiver'),(Join-Path $r 'tests'),(Join-Path $r 'tools'),(Join-Path $r 'assets')); $maisNovo=(Get-ChildItem -Path $pastas -Recurse -File -Include *.cs,*.xaml,*.csproj,*.py,*.bat,*.ps1,*.svg ^| Sort-Object LastWriteTimeUtc -Descending ^| Select-Object -First 1).LastWriteTimeUtc; $versao=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion; if($versao -ne $env:VERSAO_ESPERADA -or $maisNovo -gt (Get-Item -LiteralPath $exe).LastWriteTimeUtc){'1'}else{'0'}"') do set "PRECISA_COMPILAR=%%I"
)

if "%PRECISA_COMPILAR%"=="1" (
    echo Foi encontrada uma versao nova do Comunicador 2.2.0.
    echo Compilando e publicando antes de abrir...
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
