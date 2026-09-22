@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
set "COMUNICADOR_BUILD_ROOT=%~dp0"
cd /d "%ROOT%"

echo ===============================================
echo   Comunicador 2.5.1 - build completo
echo ===============================================

echo.
echo [1/8] Gerando o icone do aplicativo...
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\Gerar-Icone.ps1"
if errorlevel 1 goto :erro

echo.
echo [2/8] Restaurando dependencias .NET...
rem O SDK 10.0.302 pode encerrar o restore de arquivos .slnx com codigo 1 e
rem zero erros. Restaurar os dois projetos diretamente evita esse bug do SDK.
dotnet restore src\Comunicador\Comunicador.csproj
if errorlevel 1 goto :erro
dotnet restore tests\Comunicador.Tests\Comunicador.Tests.csproj
if errorlevel 1 goto :erro

echo.
echo [3/8] Preparando as dependencias dos testes Python...
python -m pip install --disable-pip-version-check --quiet -r receiver\requirements-dev.txt
if errorlevel 1 goto :erro

echo.
echo [4/8] Executando os testes do receptor Python...
set "PYTEST_TEMP=%TEMP%\Comunicador-pytest-%RANDOM%%RANDOM%"
python -m pytest receiver\tests -q -p no:cacheprovider --basetemp "!PYTEST_TEMP!"
if errorlevel 1 goto :erro
rmdir /s /q "!PYTEST_TEMP!" >nul 2>nul

echo.
echo [5/8] Executando testes C# e integracao C# com Python...
dotnet test tests\Comunicador.Tests\Comunicador.Tests.csproj -c Release --no-restore
if errorlevel 1 goto :erro

echo.
echo [6/8] Compilando o painel 2.5.1 em Release...
dotnet build src\Comunicador\Comunicador.csproj -c Release --no-restore
if errorlevel 1 goto :erro

echo.
echo [7/8] Publicando self-contained, single-file e win-x64...
rem Fecha somente uma copia que esteja rodando diretamente desta pasta dist.
rem Isso evita arquivo bloqueado ao substituir o executavel durante um upgrade.
powershell -NoProfile -Command "$exe=[IO.Path]::GetFullPath((Join-Path $env:COMUNICADOR_BUILD_ROOT 'dist\Comunicador.exe')); Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'Comunicador.exe' -and $_.ExecutablePath -eq $exe } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
if exist dist rmdir /s /q dist
dotnet publish src\Comunicador\Comunicador.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
    -o dist
if errorlevel 1 goto :erro

echo.
echo [8/8] Verificando executavel e versao...
if not exist "dist\Comunicador.exe" (
    echo ERRO: dist\Comunicador.exe nao foi gerado.
    goto :erro
)
for /f "delims=" %%V in ('powershell -NoProfile -Command "(Get-Item -LiteralPath 'dist\Comunicador.exe').VersionInfo.FileVersion"') do set "VERSAO_GERADA=%%V"
if not "!VERSAO_GERADA!"=="2.5.1.0" (
    echo ERRO: versao gerada !VERSAO_GERADA!, esperada 2.5.1.0.
    goto :erro
)

echo.
echo ===============================================
echo   Build concluido com sucesso!
echo   Executavel: dist\Comunicador.exe
echo   Versao:     !VERSAO_GERADA!
echo ===============================================
exit /b 0

:erro
if defined PYTEST_TEMP if exist "!PYTEST_TEMP!" rmdir /s /q "!PYTEST_TEMP!" >nul 2>nul
echo.
echo ===============================================
echo   Build FALHOU. Veja os erros acima.
echo ===============================================
exit /b 1
