@echo off
setlocal enabledelayedexpansion

set ROOT=%~dp0
cd /d "%ROOT%"

echo ===============================================
echo   Comunicador - build
echo ===============================================

echo.
echo [1/5] Restaurando dependencias .NET...
dotnet restore Comunicador.slnx
if errorlevel 1 goto :erro

echo.
echo [2/5] Executando testes (C# e integracao com receptor.py)...
dotnet test tests\Comunicador.Tests\Comunicador.Tests.csproj -c Release --no-restore
if errorlevel 1 goto :erro

echo.
echo [3/5] Compilando o painel (Release)...
dotnet build src\Comunicador\Comunicador.csproj -c Release --no-restore
if errorlevel 1 goto :erro

echo.
echo [4/5] Publicando versao self-contained (single-file, win-x64)...
if exist dist rmdir /s /q dist
dotnet publish src\Comunicador\Comunicador.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
    -o dist
if errorlevel 1 goto :erro

echo.
echo [5/5] Verificando resultado...
if not exist "dist\Comunicador.exe" (
    echo ERRO: dist\Comunicador.exe nao foi gerado.
    goto :erro
)

echo.
echo ===============================================
echo   Build concluido com sucesso!
echo   Executavel: dist\Comunicador.exe
echo ===============================================
exit /b 0

:erro
echo.
echo ===============================================
echo   Build FALHOU. Veja os erros acima.
echo ===============================================
exit /b 1
