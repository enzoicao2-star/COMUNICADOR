@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
cd /d "%ROOT%"

echo ===============================================
echo   Comunicador 2.2.2 - validar e subir ao GitHub
echo ===============================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo ERRO: Git nao esta instalado ou nao esta no PATH.
    goto :erro
)

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
    echo ERRO: esta pasta nao e um repositorio Git.
    goto :erro
)

echo [1/5] Compilando, testando e publicando...
call "%ROOT%build.bat"
if errorlevel 1 goto :erro

echo.
echo [2/5] Preparando o executavel da atualizacao automatica...
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\Preparar-Release.ps1" -Version "2.2.2.0"
if errorlevel 1 goto :erro

echo.
echo [3/5] Preparando as alteracoes...
git add -A
if errorlevel 1 goto :erro

git diff --cached --quiet
if errorlevel 1 (
    set "MENSAGEM=Comunicador 2.2.2 - seletor de cor, navegacao e atualizacao automatica"
    if not "%~1"=="" set "MENSAGEM=%~1"
    git commit -m "!MENSAGEM!"
    if errorlevel 1 goto :erro
) else (
    echo       Nenhuma alteracao nova para criar commit.
)

echo.
echo [4/5] Sincronizando com origin/main...
git pull --rebase origin main
if errorlevel 1 (
    echo ERRO: o Git encontrou um conflito durante o rebase.
    echo       Resolva o conflito e execute este BAT novamente.
    goto :erro
)

echo.
echo [5/5] Enviando para o GitHub...
git push origin main
if errorlevel 1 goto :erro

echo.
echo ===============================================
echo   Tudo enviado ao GitHub com sucesso.
echo   Receptor publicado: 2.2.1
echo   Painel publicado:    2.2.2
echo ===============================================
echo Esta janela fecha sozinha em 5 segundos...
ping -n 6 127.0.0.1 >nul 2>nul
exit /b 0

:erro
echo.
echo ===============================================
echo   Nao foi possivel concluir. Leia o erro acima.
echo ===============================================
pause
exit /b 1
