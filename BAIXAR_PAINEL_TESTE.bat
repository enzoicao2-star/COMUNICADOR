@echo off
setlocal
set "BASE=https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/teste"
set "DIR=%LOCALAPPDATA%\Comunicador-Teste\app"
set "MANIFEST=%TEMP%\Comunicador-Teste-manifest.json"
set "NOVO=%TEMP%\Comunicador-Teste.exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop'; $base=$env:BASE; $m=Join-Path $env:TEMP 'Comunicador-Teste-manifest.json'; $x=Join-Path $env:TEMP 'Comunicador-Teste.exe'; Invoke-WebRequest -UseBasicParsing ($base+'/panel-version.json') -OutFile $m; $data=Get-Content -Raw $m | ConvertFrom-Json; Invoke-WebRequest -UseBasicParsing $data.download_url -OutFile $x; $sha=(Get-FileHash -Algorithm SHA256 $x).Hash; if($sha -ne $data.sha256){throw 'A validacao SHA-256 falhou.'}; $actual=(Get-Item $x).VersionInfo.FileVersion; if($actual -ne $data.version){throw 'A versao do executavel nao corresponde ao manifesto.'}; New-Item -ItemType Directory -Force -Path $env:DIR | Out-Null; Move-Item -Force $x (Join-Path $env:DIR 'Comunicador-Teste.exe'); $fw=Join-Path $env:DIR 'LIBERAR_FIREWALL_TESTE.bat'; Invoke-WebRequest -UseBasicParsing ($base+'/LIBERAR_FIREWALL_TESTE.bat') -OutFile $fw; try{Start-Process -FilePath $fw -Verb RunAs -Wait}catch{}; Start-Process (Join-Path $env:DIR 'Comunicador-Teste.exe')"
if errorlevel 1 (
    echo Nao foi possivel baixar ou validar o painel de teste.
    pause
    exit /b 1
)
exit /b 0
