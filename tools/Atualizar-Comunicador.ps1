param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [string]$InstallRoot = '',
    [string]$LauncherPath = '',
    [string]$ManifestUrl = 'https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/panel-version.json',
    [string]$RepositoryArchiveUrl = 'https://github.com/enzoicao2-star/COMUNICADOR/archive/refs/heads/main.zip',
    [string]$ProgressPath = '',
    [int]$PanelProcessId = 0,
    [switch]$SkipShortcuts,
    [switch]$RestartAfterUpdate,
    [switch]$ForceReinstall
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$script:ProgressPath = $ProgressPath

function Write-UpdateProgress([string]$Phase, [int]$Percent, [string]$Message, [int]$RemainingSeconds = 0) {
    if ([string]::IsNullOrWhiteSpace($script:ProgressPath)) { return }
    try {
        $parent = Split-Path -Parent $script:ProgressPath
        if (-not [string]::IsNullOrWhiteSpace($parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
        $temporary = $script:ProgressPath + '.tmp'
        $state = [ordered]@{ phase=$Phase; percent=[Math]::Max(0,[Math]::Min(100,$Percent));
            message=$Message; remaining_seconds=[Math]::Max(0,$RemainingSeconds) }
        [IO.File]::WriteAllText($temporary,($state | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $script:ProgressPath -Force
    }
    catch { }
}

# Windows PowerShell 5.1 nem sempre carrega System.Net.Http automaticamente.
# Faça a carga depois de definir a gravação de progresso para que qualquer falha
# apareça na janela em vez de deixá-la girando sem informação.
try {
    Add-Type -AssemblyName System.Net.Http
}
catch {
    Write-UpdateProgress 'failed' 100 ('Não foi possível preparar o download: ' + $_.Exception.Message)
    Write-Host ('Falha ao preparar o atualizador: ' + $_.Exception.Message)
    exit 3
}

function Download-FileWithProgress(
    [string]$Uri, [string]$Destination, [string]$Message, [int]$StartPercent, [int]$EndPercent
) {
    $client = [System.Net.Http.HttpClient]::new()
    $response = $null
    $source = $null
    $destinationStream = $null
    try {
        $requestUri = [Uri]$Uri
        $response = $client.GetAsync($requestUri,[System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $response.EnsureSuccessStatusCode()
        $total = $response.Content.Headers.ContentLength
        $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $destinationStream = [IO.File]::Open($Destination,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
        $buffer = New-Object byte[] 65536
        [long]$downloaded = 0
        while ($true) {
            $read = $source.ReadAsync($buffer,0,$buffer.Length).GetAwaiter().GetResult()
            if ($read -le 0) { break }
            $destinationStream.Write($buffer,0,$read)
            $downloaded += $read
            if ($total -gt 0) {
                $ratio = [Math]::Min(1.0,($downloaded / [double]$total))
                $percent = $StartPercent + [int][Math]::Floor(($EndPercent-$StartPercent)*$ratio)
                Write-UpdateProgress 'running' $percent $Message
            }
        }
        if ($total -gt 0 -and $downloaded -ne $total) { throw 'O download terminou incompleto.' }
        Write-UpdateProgress 'running' $EndPercent $Message
    }
    finally {
        if ($null -ne $destinationStream) { $destinationStream.Dispose() }
        if ($null -ne $source) { $source.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
        $client.Dispose()
    }
}

function Get-VersionOrZero([string]$Value) {
    try { return [version]$Value }
    catch { return [version]'0.0.0.0' }
}

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
        finally { $algorithm.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Read-JsonOrNull([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { return $null }
}

function Restore-PreviousPanel([string]$TargetPath, [string]$BackupPath,
    [string]$PendingPath, [string]$HealthPath, [string]$FailurePath, [object]$Pending) {
    if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) { return $false }
    Get-CimInstance Win32_Process -Filter "Name='Comunicador.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -eq $TargetPath } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $TargetPath -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $BackupPath -Destination $TargetPath -Force -ErrorAction Stop
    if ($null -ne $Pending) {
        [ordered]@{ version = [string]$Pending.version; sha256 = [string]$Pending.sha256;
            failed_at = [DateTimeOffset]::UtcNow.ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath $FailurePath -Encoding UTF8
    }
    Remove-Item -LiteralPath $PendingPath,$HealthPath -Force -ErrorAction SilentlyContinue
    Write-Host 'A nova versao nao iniciou corretamente. A versao anterior foi restaurada.'
    return $true
}

function Add-CacheBuster([string]$Url, [string]$Name, [string]$Value) {
    $separator = if ($Url.Contains('?')) { '&' } else { '?' }
    return $Url + $separator + $Name + '=' + [Uri]::EscapeDataString($Value)
}

function Get-SafeInstallPath([string]$Root, [string]$RelativePath) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $candidate = [IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    if (-not $candidate.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ('Caminho invalido no pacote: ' + $RelativePath)
    }
    return $candidate
}

function Read-InstallMetadata([string]$Root) {
    $metadataPath = Join-Path $Root 'instalacao.json'
    if (-not (Test-Path -LiteralPath $metadataPath)) { return $null }
    try { return Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json }
    catch { return $null }
}

function Get-InstallInventory([object]$Metadata) {
    if ($null -eq $Metadata -or $null -eq $Metadata.files) { return @() }
    return @($Metadata.files | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Test-ReceiverInstalled {
    $appDirectory = Join-Path $env:LOCALAPPDATA 'Comunicador\Receptor\app'
    return (Test-Path -LiteralPath (Join-Path $appDirectory 'receptor.py') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $appDirectory 'protocolo.py') -PathType Leaf)
}

function Test-CompleteInstall([string]$Root, [object]$Metadata) {
    $required = @(
        'ABRIR_COMUNICADOR.bat',
        'DESINSTALAR_COMUNICADOR.bat',
        'LIBERAR_FIREWALL.bat',
        'REVERTER_CONFIGURACOES.bat',
        'README.md',
        'PROTOCOLO.md',
        'release\Comunicador.exe',
        'release\panel-version.json',
        'tools\Atualizar-Comunicador.ps1',
        'receiver\INSTALAR_RECEPTOR.bat',
        'src\Comunicador\Comunicador.csproj'
    )
    $inventory = Get-InstallInventory $Metadata
    if ($null -eq $Metadata -or $inventory.Count -eq 0) { return $false }
    foreach ($relativePath in @($required + $inventory)) {
        if ($relativePath -eq 'receiver\INSTALAR_RECEPTOR.bat' -and (Test-ReceiverInstalled)) { continue }
        try { $path = Get-SafeInstallPath $Root $relativePath }
        catch { return $false }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    }
    return $true
}

function Test-PowerShellFile([string]$Path) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) {
        throw ('O atualizador do pacote possui erro de sintaxe: ' + $errors[0].Message)
    }
}

function Install-RepositorySnapshot(
    [string]$Root,
    [string]$ArchiveUrl,
    [string]$CurrentLauncher,
    [bool]$FreshInstall
) {
    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('Comunicador-instalacao-' + [guid]::NewGuid().ToString('N'))
    $archivePath = Join-Path $temporaryRoot 'projeto.zip'
    $extractPath = Join-Path $temporaryRoot 'extraido'
    try {
        New-Item -ItemType Directory -Force -Path $extractPath | Out-Null
        Write-Host 'Baixando todos os arquivos do Comunicador publicados no GitHub...'
        Write-UpdateProgress 'running' 7 'Baixando os arquivos auxiliares...'
        $archiveRequest = Add-CacheBuster $ArchiveUrl 't' ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString())
        Download-FileWithProgress $archiveRequest $archivePath 'Baixando os arquivos do programa...' 8 42
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force

        $topLevelDirectories = @(Get-ChildItem -LiteralPath $extractPath -Directory -Force)
        $topLevelFiles = @(Get-ChildItem -LiteralPath $extractPath -File -Force)
        if ($topLevelDirectories.Count -eq 1 -and $topLevelFiles.Count -eq 0) {
            $sourceRoot = $topLevelDirectories[0].FullName
        }
        else {
            $sourceRoot = $extractPath
        }

        $requiredSnapshotFiles = @(
            'ABRIR_COMUNICADOR.bat',
            'DESINSTALAR_COMUNICADOR.bat',
            'LIBERAR_FIREWALL.bat',
            'REVERTER_CONFIGURACOES.bat',
            'release\Comunicador.exe',
            'release\panel-version.json',
            'tools\Atualizar-Comunicador.ps1'
        )
        foreach ($relativePath in $requiredSnapshotFiles) {
            $sourcePath = Join-Path $sourceRoot $relativePath
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or (Get-Item -LiteralPath $sourcePath).Length -eq 0) {
                throw ('O pacote baixado nao contem o arquivo obrigatorio ' + $relativePath + '.')
            }
        }
        Test-PowerShellFile (Join-Path $sourceRoot 'tools\Atualizar-Comunicador.ps1')

        New-Item -ItemType Directory -Force -Path $Root | Out-Null
        $launcherFull = if ([string]::IsNullOrWhiteSpace($CurrentLauncher)) { '' } else { [IO.Path]::GetFullPath($CurrentLauncher) }
        $pendingLauncher = $null
        $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force)
        $copyIndex = 0
        foreach ($sourceFile in $sourceFiles) {
            $copyIndex++
            $relativePath = $sourceFile.FullName.Substring($sourceRoot.Length).TrimStart('\')
            $destinationPath = Get-SafeInstallPath $Root $relativePath
            if ($relativePath -eq 'receiver\INSTALAR_RECEPTOR.bat' -and (Test-ReceiverInstalled)) {
                Remove-Item -LiteralPath $destinationPath -Force -ErrorAction SilentlyContinue
                continue
            }
            $destinationDirectory = Split-Path -Parent $destinationPath
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

            $isRunningLauncher = -not [string]::IsNullOrWhiteSpace($launcherFull) -and
                $destinationPath.Equals($launcherFull, [StringComparison]::OrdinalIgnoreCase)
            if ($isRunningLauncher) {
                $pendingLauncher = Join-Path $Root 'ABRIR_COMUNICADOR.pending.bat'
                Copy-Item -LiteralPath $sourceFile.FullName -Destination $pendingLauncher -Force
                Write-UpdateProgress 'running' (42 + [int][Math]::Floor(20*$copyIndex/[Math]::Max(1,$sourceFiles.Count)) ) 'Preparando os arquivos do Comunicador...'
                continue
            }
            $isBatchFile = $sourceFile.Extension.Equals('.bat', [StringComparison]::OrdinalIgnoreCase)
            $isUpdater = $relativePath.Equals(
                'tools\Atualizar-Comunicador.ps1', [StringComparison]::OrdinalIgnoreCase)
            $isManifest = $relativePath.Equals(
                'release\panel-version.json', [StringComparison]::OrdinalIgnoreCase)
            if ($FreshInstall -or $isBatchFile -or $isUpdater -or $isManifest `
                -or -not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
                Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath -Force
            }
            Write-UpdateProgress 'running' (42 + [int][Math]::Floor(20*$copyIndex/[Math]::Max(1,$sourceFiles.Count)) ) 'Preparando os arquivos do Comunicador...'
        }

        if ($null -ne $pendingLauncher -and -not [string]::IsNullOrWhiteSpace($launcherFull)) {
            # O CMD ainda está executando o BAT atual. Um processo oculto espera esse
            # CMD terminar e só então troca o launcher, evitando cortá-lo no meio.
            $parentProcessId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -ErrorAction SilentlyContinue).ParentProcessId
            if ($parentProcessId) {
                $pendingQuoted = $pendingLauncher.Replace("'", "''")
                $launcherQuoted = $launcherFull.Replace("'", "''")
                $deferred = @"
try { Wait-Process -Id $parentProcessId -Timeout 120 -ErrorAction SilentlyContinue } catch {}
for (`$attempt = 0; `$attempt -lt 20; `$attempt++) {
    try {
        Copy-Item -LiteralPath '$pendingQuoted' -Destination '$launcherQuoted' -Force -ErrorAction Stop
        Remove-Item -LiteralPath '$pendingQuoted' -Force -ErrorAction SilentlyContinue
        break
    }
    catch { Start-Sleep -Milliseconds 500 }
}
"@
                $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($deferred))
                Start-Process -FilePath 'powershell.exe' `
                    -ArgumentList @('-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden', '-EncodedCommand', $encoded) `
                    -WindowStyle Hidden
            }
        }

        foreach ($relativePath in $requiredSnapshotFiles) {
            if (-not (Test-Path -LiteralPath (Get-SafeInstallPath $Root $relativePath) -PathType Leaf)) {
                throw ('A instalacao nao conseguiu gravar ' + $relativePath + '.')
            }
        }

        return @($sourceFiles | ForEach-Object {
            $_.FullName.Substring($sourceRoot.Length).TrimStart('\')
        } | Sort-Object -Unique)
    }
    finally {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Write-InstallMetadata(
    [string]$Root,
    [version]$Version,
    [string[]]$Inventory,
    [object]$PreviousMetadata
) {
    if ($Inventory.Count -eq 0) { return }
    $installedAt = [DateTimeOffset]::UtcNow.ToString('o')
    if ($null -ne $PreviousMetadata -and -not [string]::IsNullOrWhiteSpace([string]$PreviousMetadata.installed_at)) {
        $installedAt = [string]$PreviousMetadata.installed_at
    }
    $metadata = [ordered]@{
        product = 'Comunicador'
        version = $Version.ToString()
        installed_at = $installedAt
        last_verified_at = [DateTimeOffset]::UtcNow.ToString('o')
        source = 'https://github.com/enzoicao2-star/COMUNICADOR'
        install_path = [IO.Path]::GetFullPath($Root)
        files = @($Inventory | Sort-Object -Unique)
    }
    $metadataPath = Join-Path $Root 'instalacao.json'
    $metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
}

function Install-Shortcuts([string]$Root, [string]$ExePath) {
    if ($SkipShortcuts -or $env:COMUNICADOR_SKIP_SHORTCUTS -eq '1') { return }
    $installedLauncher = Join-Path $Root 'ABRIR_COMUNICADOR.bat'
    if (-not (Test-Path -LiteralPath $installedLauncher -PathType Leaf)) { return }
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcutLocations = @(
            (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) 'Comunicador.lnk'),
            (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'Comunicador.lnk')
        )
        foreach ($shortcutPath in $shortcutLocations) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $shortcutPath) | Out-Null
            $shortcut = $shell.CreateShortcut($shortcutPath)
            # O atalho deve apontar para o aplicativo: se apontar para cmd.exe,
            # o Windows pode exibir o ícone genérico na barra de tarefas.
            $shortcut.TargetPath = $ExePath
            $shortcut.Arguments = ''
            $shortcut.WorkingDirectory = Split-Path -Parent $ExePath
            $shortcut.IconLocation = $ExePath + ',0'
            $shortcut.Description = 'Abrir o Comunicador'
            $shortcut.WindowStyle = 7
            $shortcut.Save()
        }
    }
    catch {
        Write-Host ('AVISO: o painel foi instalado, mas nao foi possivel criar os atalhos: ' + $_.Exception.Message)
    }
}

$target = [IO.Path]::GetFullPath($ExecutablePath)
$targetDirectory = Split-Path -Parent $target
$backupPath = Join-Path $targetDirectory 'Comunicador.anterior.exe'
$pendingPath = Join-Path $targetDirectory 'Comunicador.atualizacao-pendente.json'
$healthPath = Join-Path $targetDirectory 'Comunicador.inicializacao-ok.json'
$failurePath = Join-Path $targetDirectory 'Comunicador.atualizacao-falhou.json'
$managedRoot = $null
if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
    $managedRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
    $expectedTarget = [IO.Path]::GetFullPath((Join-Path $managedRoot 'release\Comunicador.exe'))
    if (-not $target.Equals($expectedTarget, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host 'Falha na instalacao: o executavel nao pertence a pasta de instalacao informada.'
        exit 5
    }
}

$previousPending = Read-JsonOrNull $pendingPath
if ($null -ne $previousPending) {
    $previousHealth = Read-JsonOrNull $healthPath
    $confirmed = $null -ne $previousHealth -and
        [string]$previousHealth.token -eq [string]$previousPending.token -and
        [string]$previousHealth.version -eq [string]$previousPending.version -and
        (Test-Path -LiteralPath $target -PathType Leaf)
    if ($confirmed) {
        $confirmed = (Get-Sha256 $target).ToUpperInvariant() -eq [string]$previousPending.sha256
    }
    if ($confirmed) {
        Remove-Item -LiteralPath $backupPath,$pendingPath,$healthPath -Force -ErrorAction SilentlyContinue
    }
    elseif (Test-Path -LiteralPath $backupPath -PathType Leaf) {
        $started = [DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse([string]$previousPending.started_at, [ref]$started) -and
            [DateTimeOffset]::UtcNow - $started -lt [TimeSpan]::FromSeconds(45)) {
            Write-Host 'A nova versao ainda esta iniciando; mantendo a copia anterior ate a confirmacao.'
            exit 0
        }
        Restore-PreviousPanel $target $backupPath $pendingPath $healthPath $failurePath $previousPending | Out-Null
    }
    elseif ($null -ne $previousHealth) {
        Remove-Item -LiteralPath $pendingPath,$healthPath -Force -ErrorAction SilentlyContinue
    }
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-UpdateProgress 'running' 2 'Verificando a versão publicada...'
    $manifestRequest = Add-CacheBuster $ManifestUrl 't' ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString())
    $manifest = Invoke-RestMethod -UseBasicParsing -Headers @{ 'Cache-Control' = 'no-cache' } -Uri $manifestRequest
    $latestVersion = Get-VersionOrZero ([string]$manifest.version)
    $rollbackFromVersion = Get-VersionOrZero ([string]$manifest.rollback_from_version)
    $downloadUrl = [string]$manifest.download_url
    $expectedHash = ([string]$manifest.sha256).Trim().ToUpperInvariant()
    if ($latestVersion -eq [version]'0.0.0.0' -or [string]::IsNullOrWhiteSpace($downloadUrl) `
        -or $expectedHash -notmatch '^[A-F0-9]{64}$') {
        throw 'O manifesto publicado esta incompleto.'
    }
    Write-UpdateProgress 'running' 5 ('Preparando a versão ' + $latestVersion + '...')
}
catch {
    if (Test-Path -LiteralPath $target) {
        $fallbackVersion = Get-VersionOrZero (Get-Item -LiteralPath $target).VersionInfo.FileVersion
        Write-UpdateProgress 'failed' 100 ('Não foi possível verificar a atualização: ' + $_.Exception.Message)
        Write-Host ('Sem acesso ao GitHub; abrindo a versao instalada ' + $fallbackVersion + '.')
        exit 0
    }
    Write-UpdateProgress 'failed' 100 ('Falha ao consultar a versão publicada: ' + $_.Exception.Message)
    Write-Host ('Falha ao consultar a versao publicada: ' + $_.Exception.Message)
    exit 2
}

$metadata = $null
$inventory = @()
if ($null -ne $managedRoot) {
    $metadata = Read-InstallMetadata $managedRoot
    $inventory = Get-InstallInventory $metadata
    $freshInstall = -not (Test-Path -LiteralPath (Join-Path $managedRoot 'release\Comunicador.exe') -PathType Leaf)
    try {
        $inventory = Install-RepositorySnapshot $managedRoot $RepositoryArchiveUrl $LauncherPath $freshInstall
        if ($freshInstall) {
            Write-Host ('Projeto completo instalado em ' + $managedRoot + '.')
        }
        elseif (-not (Test-CompleteInstall $managedRoot $metadata)) {
            Write-Host 'Arquivos ausentes da instalacao foram restaurados.'
        }
        else {
            Write-Host 'Arquivos BAT e auxiliares sincronizados com o GitHub.'
        }
    }
    catch {
        Write-Host ('Falha ao sincronizar os arquivos do GitHub: ' + $_.Exception.Message)
        if ($freshInstall -or -not (Test-Path -LiteralPath $target)) { exit 4 }
    }
}

$currentVersion = [version]'0.0.0.0'
$currentHash = ''
if (Test-Path -LiteralPath $target) {
    $currentVersion = Get-VersionOrZero (Get-Item -LiteralPath $target).VersionInfo.FileVersion
    if ($null -ne $managedRoot -and $currentVersion -eq $latestVersion) {
        try { $currentHash = (Get-Sha256 $target).ToUpperInvariant() }
        catch { $currentHash = '' }
    }
}

$previousFailure = Read-JsonOrNull $failurePath
$rollbackRequested = $rollbackFromVersion -eq $currentVersion -and $latestVersion -lt $currentVersion
$skipKnownFailure = -not $ForceReinstall -and
    $null -ne $previousFailure -and
    [string]$previousFailure.version -eq $latestVersion.ToString() -and
    [string]$previousFailure.sha256 -eq $expectedHash
$isCurrent = $skipKnownFailure -or -not $ForceReinstall -and (Test-Path -LiteralPath $target) -and (
    ($currentVersion -gt $latestVersion -and -not $rollbackRequested) -or
    ($currentVersion -eq $latestVersion -and ($null -eq $managedRoot -or $currentHash -eq $expectedHash))
)
if ($isCurrent) {
    if ($null -ne $managedRoot) {
        if ($inventory.Count -eq 0) { $inventory = Get-InstallInventory (Read-InstallMetadata $managedRoot) }
        Write-InstallMetadata $managedRoot $currentVersion $inventory $metadata
        Install-Shortcuts $managedRoot $target
    }
    if ($skipKnownFailure) { Write-Host ('A versao ' + $latestVersion + ' falhou antes; mantendo a versao ' + $currentVersion + '.') }
    else { Write-Host ('Comunicador ' + $currentVersion + ' ja esta atualizado.') }
    Write-UpdateProgress 'up_to_date' 100 'Este Comunicador já está na versão mais recente.'
    exit 0
}

if ($rollbackRequested) {
    Write-Host ('Revertendo a versao bloqueada ' + $currentVersion + ' para a versao compativel ' + $latestVersion + '.')
}
else { Write-Host ('Nova versao encontrada no GitHub: ' + $latestVersion + '.') }
Write-Host 'Baixando e validando antes de substituir a copia instalada...'
New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
$downloadPath = Join-Path $targetDirectory 'Comunicador.download.exe'

try {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    $downloadRequest = Add-CacheBuster $downloadUrl 'v' $latestVersion.ToString()
    Download-FileWithProgress $downloadRequest $downloadPath 'Baixando a nova versão do Comunicador...' 70 92

    Write-UpdateProgress 'running' 94 'Validando a atualização...'
    $actualHash = (Get-Sha256 $downloadPath).ToUpperInvariant()
    if ($actualHash -ne $expectedHash) {
        throw 'O SHA-256 do arquivo baixado nao corresponde ao manifesto.'
    }
    $downloadedVersion = Get-VersionOrZero (Get-Item -LiteralPath $downloadPath).VersionInfo.FileVersion
    if ($downloadedVersion -ne $latestVersion) {
        throw ('O arquivo baixado informa a versao ' + $downloadedVersion + ', esperada ' + $latestVersion + '.')
    }

    if ($RestartAfterUpdate) {
        Write-UpdateProgress 'download_complete' 100 'Download da nova atualização concluído.'
        Start-Sleep -Seconds 5
        for ($remaining = 10; $remaining -ge 0; $remaining--) {
            Write-UpdateProgress 'restart_wait' 100 'O Comunicador será reiniciado em' $remaining
            if ($remaining -gt 0) { Start-Sleep -Seconds 1 }
        }
        Start-Sleep -Milliseconds 450
    }

    Get-CimInstance Win32_Process -Filter "Name='Comunicador.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -eq $target } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backupPath -Force }
    try {
        Move-Item -LiteralPath $downloadPath -Destination $target -Force
        Remove-Item -LiteralPath $healthPath -Force -ErrorAction SilentlyContinue
        $pending = [ordered]@{
            version = $latestVersion.ToString()
            sha256 = $expectedHash
            token = [guid]::NewGuid().ToString('N')
            started_at = [DateTimeOffset]::UtcNow.ToString('o')
        }
        $pending | ConvertTo-Json | Set-Content -LiteralPath $pendingPath -Encoding UTF8
    }
    catch {
        if (Test-Path -LiteralPath $backupPath) {
            Move-Item -LiteralPath $backupPath -Destination $target -Force
        }
        throw
    }

    if ($null -ne $managedRoot) {
        if ($inventory.Count -eq 0) { $inventory = Get-InstallInventory (Read-InstallMetadata $managedRoot) }
        Write-InstallMetadata $managedRoot $latestVersion $inventory $metadata
        Install-Shortcuts $managedRoot $target
    }
    Write-Host ('Comunicador atualizado automaticamente para ' + $latestVersion + '.')
    if ($currentVersion -gt [version]'0.0.0.0') {
        try {
            $noticeDirectory = Join-Path $env:APPDATA 'Comunicador'
            New-Item -ItemType Directory -Force -Path $noticeDirectory | Out-Null
            $notice = [ordered]@{
                version = $latestVersion.ToString()
                previous_version = $currentVersion.ToString()
                summary = [string]$manifest.summary
                changes = @($manifest.changes)
            }
            $notice | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $noticeDirectory 'ultima-atualizacao.json') -Encoding UTF8
        }
        catch { Write-Host ('AVISO: nao foi possivel salvar o resumo da atualizacao: ' + $_.Exception.Message) }
    }
    if ($RestartAfterUpdate) {
        Write-UpdateProgress 'launching' 100 'Reiniciando o Comunicador...'
        if ($PanelProcessId -gt 0) {
            try { Wait-Process -Id $PanelProcessId -Timeout 20 -ErrorAction SilentlyContinue } catch { }
        }
        $startedPanel = Start-Process -FilePath $target -WorkingDirectory $targetDirectory -PassThru
        $healthy = $false
        for ($attempt = 0; $attempt -lt 90; $attempt++) {
            $health = Read-JsonOrNull $healthPath
            if ($null -ne $health -and [string]$health.token -eq [string]$pending.token -and
                [string]$health.version -eq $latestVersion.ToString()) {
                $healthy = $true
                break
            }
            $startedPanel.Refresh()
            if ($startedPanel.HasExited) { break }
            Start-Sleep -Milliseconds 500
        }
        if (-not $healthy) {
            if (-not $startedPanel.HasExited) { Stop-Process -Id $startedPanel.Id -Force -ErrorAction SilentlyContinue }
            Restore-PreviousPanel $target $backupPath $pendingPath $healthPath $failurePath $pending | Out-Null
            Remove-Item -LiteralPath (Join-Path $env:APPDATA 'Comunicador\ultima-atualizacao.json') -Force -ErrorAction SilentlyContinue
            if ([string]::IsNullOrWhiteSpace($LauncherPath) -and (Test-Path -LiteralPath $target)) {
                Start-Process -FilePath $target -WorkingDirectory $targetDirectory | Out-Null
            }
            throw 'A nova versao nao confirmou a inicializacao em 45 segundos.'
        }
        Remove-Item -LiteralPath $backupPath,$pendingPath,$healthPath,$failurePath -Force -ErrorAction SilentlyContinue
        Write-UpdateProgress 'done' 100 'Atualização concluída. O Comunicador foi reiniciado.'
    }
    if ($RestartAfterUpdate) { exit 10 }
    exit 0
}
catch {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $pendingPath) -and (Test-Path -LiteralPath $backupPath)) {
        Restore-PreviousPanel $target $backupPath $pendingPath $healthPath $failurePath (Read-JsonOrNull $pendingPath) | Out-Null
    }
    Write-UpdateProgress 'failed' 100 ('Falha na atualização: ' + $_.Exception.Message)
    Write-Host ('Falha na atualizacao automatica: ' + $_.Exception.Message)
    if (Test-Path -LiteralPath $target) { exit 3 }
    exit 3
}
