param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [string]$InstallRoot = '',
    [string]$LauncherPath = '',
    [string]$ManifestUrl = 'https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/panel-version.json',
    [string]$RepositoryArchiveUrl = 'https://github.com/enzoicao2-star/COMUNICADOR/archive/refs/heads/main.zip',
    [switch]$SkipShortcuts,
    [switch]$RestartAfterUpdate
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

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

function Test-CompleteInstall([string]$Root, [object]$Metadata) {
    $required = @(
        'ABRIR_COMUNICADOR.bat',
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
        $archiveRequest = Add-CacheBuster $ArchiveUrl 't' ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString())
        Invoke-WebRequest -UseBasicParsing -Headers @{ 'Cache-Control' = 'no-cache' } `
            -Uri $archiveRequest -OutFile $archivePath -ErrorAction Stop
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
        foreach ($sourceFile in $sourceFiles) {
            $relativePath = $sourceFile.FullName.Substring($sourceRoot.Length).TrimStart('\')
            $destinationPath = Get-SafeInstallPath $Root $relativePath
            $destinationDirectory = Split-Path -Parent $destinationPath
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

            $isRunningLauncher = -not [string]::IsNullOrWhiteSpace($launcherFull) -and
                $destinationPath.Equals($launcherFull, [StringComparison]::OrdinalIgnoreCase)
            if ($isRunningLauncher) {
                $pendingLauncher = Join-Path $Root 'ABRIR_COMUNICADOR.pending.bat'
                Copy-Item -LiteralPath $sourceFile.FullName -Destination $pendingLauncher -Force
                continue
            }
            $isBatchFile = $sourceFile.Extension.Equals('.bat', [StringComparison]::OrdinalIgnoreCase)
            $isUpdater = $relativePath.Equals(
                'tools\Atualizar-Comunicador.ps1', [StringComparison]::OrdinalIgnoreCase)
            if ($FreshInstall -or $isBatchFile -or $isUpdater `
                -or -not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
                Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath -Force
            }
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
            $shortcut.TargetPath = $env:ComSpec
            $shortcut.Arguments = '/d /c ""' + $installedLauncher + '""'
            $shortcut.WorkingDirectory = $Root
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
$managedRoot = $null
if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
    $managedRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
    $expectedTarget = [IO.Path]::GetFullPath((Join-Path $managedRoot 'release\Comunicador.exe'))
    if (-not $target.Equals($expectedTarget, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host 'Falha na instalacao: o executavel nao pertence a pasta de instalacao informada.'
        exit 5
    }
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $manifestRequest = Add-CacheBuster $ManifestUrl 't' ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString())
    $manifest = Invoke-RestMethod -UseBasicParsing -Headers @{ 'Cache-Control' = 'no-cache' } -Uri $manifestRequest
    $latestVersion = Get-VersionOrZero ([string]$manifest.version)
    $downloadUrl = [string]$manifest.download_url
    $expectedHash = ([string]$manifest.sha256).Trim().ToUpperInvariant()
    if ($latestVersion -eq [version]'0.0.0.0' -or [string]::IsNullOrWhiteSpace($downloadUrl) `
        -or $expectedHash -notmatch '^[A-F0-9]{64}$') {
        throw 'O manifesto publicado esta incompleto.'
    }
}
catch {
    if (Test-Path -LiteralPath $target) {
        $fallbackVersion = Get-VersionOrZero (Get-Item -LiteralPath $target).VersionInfo.FileVersion
        Write-Host ('Sem acesso ao GitHub; abrindo a versao instalada ' + $fallbackVersion + '.')
        exit 0
    }
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

$isCurrent = (Test-Path -LiteralPath $target) -and (
    $currentVersion -gt $latestVersion -or
    ($currentVersion -eq $latestVersion -and ($null -eq $managedRoot -or $currentHash -eq $expectedHash))
)
if ($isCurrent) {
    if ($null -ne $managedRoot) {
        if ($inventory.Count -eq 0) { $inventory = Get-InstallInventory (Read-InstallMetadata $managedRoot) }
        Write-InstallMetadata $managedRoot $currentVersion $inventory $metadata
        Install-Shortcuts $managedRoot $target
    }
    Write-Host ('Comunicador ' + $currentVersion + ' ja esta atualizado.')
    exit 0
}

Write-Host ('Nova versao encontrada no GitHub: ' + $latestVersion + '.')
Write-Host 'Baixando e validando antes de substituir a copia instalada...'
New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
$downloadPath = Join-Path $targetDirectory 'Comunicador.download.exe'
$backupPath = Join-Path $targetDirectory 'Comunicador.anterior.exe'

try {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    $downloadRequest = Add-CacheBuster $downloadUrl 'v' $latestVersion.ToString()
    Invoke-WebRequest -UseBasicParsing -Uri $downloadRequest -OutFile $downloadPath -ErrorAction Stop

    $actualHash = (Get-Sha256 $downloadPath).ToUpperInvariant()
    if ($actualHash -ne $expectedHash) {
        throw 'O SHA-256 do arquivo baixado nao corresponde ao manifesto.'
    }
    $downloadedVersion = Get-VersionOrZero (Get-Item -LiteralPath $downloadPath).VersionInfo.FileVersion
    if ($downloadedVersion -ne $latestVersion) {
        throw ('O arquivo baixado informa a versao ' + $downloadedVersion + ', esperada ' + $latestVersion + '.')
    }

    Get-CimInstance Win32_Process -Filter "Name='Comunicador.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -eq $target } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backupPath -Force }
    try {
        Move-Item -LiteralPath $downloadPath -Destination $target -Force
        Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
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
    if ($RestartAfterUpdate) {
        try {
            $noticeDirectory = Join-Path $env:APPDATA 'Comunicador'
            New-Item -ItemType Directory -Force -Path $noticeDirectory | Out-Null
            $notice = [ordered]@{
                version = $latestVersion.ToString()
                summary = [string]$manifest.summary
                changes = @($manifest.changes)
            }
            $notice | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $noticeDirectory 'ultima-atualizacao.json') -Encoding UTF8
        }
        catch { Write-Host ('AVISO: nao foi possivel salvar o resumo da atualizacao: ' + $_.Exception.Message) }
        Start-Process -FilePath $target -WorkingDirectory $targetDirectory
    }
    exit 0
}
catch {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    Write-Host ('Falha na atualizacao automatica: ' + $_.Exception.Message)
    if (Test-Path -LiteralPath $target) { exit 0 }
    exit 3
}
