$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'Comunicador'
$identityPath = Join-Path $base 'device.json'
$sessionPath = Join-Path $base 'cloud_session.json'
$supabaseUrl = 'https://yofxuiajeyxeacophgdy.supabase.co'
$supabaseKey = 'sb_publishable_9vE6ehPLNhoByGInnUAlug_Ndd_fTam'

try {
    if (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) { exit 0 }
    $identity = Get-Content -LiteralPath $identityPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$identity.device_id)) { exit 0 }

    $identity | Add-Member -NotePropertyName has_panel -NotePropertyValue $false -Force
    $identity | Add-Member -NotePropertyName panel_version -NotePropertyValue $null -Force
    $identity | Add-Member -NotePropertyName panel_uninstalled -NotePropertyValue $true -Force
    $identityTemp = $identityPath + '.tmp'
    $identityJson = ConvertTo-Json -InputObject $identity -Depth 10
    [System.IO.File]::WriteAllText($identityTemp, $identityJson, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $identityTemp -Destination $identityPath -Force

    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) { exit 0 }
    $session = Get-Content -LiteralPath $sessionPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $accessToken = [string]$session.access_token
    if (-not [string]::IsNullOrWhiteSpace([string]$session.refresh_token)) {
        try {
            $refreshed = Invoke-RestMethod -Uri ($supabaseUrl + '/auth/v1/token?grant_type=refresh_token') `
                -Method Post -Headers @{ apikey = $supabaseKey } -ContentType 'application/json' `
                -Body (ConvertTo-Json -Compress @{ refresh_token = [string]$session.refresh_token }) `
                -TimeoutSec 12
            if (-not [string]::IsNullOrWhiteSpace([string]$refreshed.access_token)) {
                $refreshed | Add-Member -NotePropertyName expires_at `
                    -NotePropertyValue ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + [int]$refreshed.expires_in) -Force
                $sessionTemp = $sessionPath + '.tmp'
                [System.IO.File]::WriteAllText($sessionTemp, (ConvertTo-Json -InputObject $refreshed -Depth 10), [System.Text.UTF8Encoding]::new($false))
                Move-Item -LiteralPath $sessionTemp -Destination $sessionPath -Force
                $accessToken = [string]$refreshed.access_token
            }
        } catch {
            if ([string]::IsNullOrWhiteSpace($accessToken)) { exit 0 }
        }
    }
    if ([string]::IsNullOrWhiteSpace($accessToken)) { exit 0 }

    $headers = @{ apikey = $supabaseKey; Authorization = ('Bearer ' + $accessToken) }
    Invoke-WebRequest -UseBasicParsing -Uri ($supabaseUrl + '/rest/v1/rpc/unregister_panel') `
        -Method Post -Headers $headers -ContentType 'application/json' `
        -Body (ConvertTo-Json -Compress @{ p_device_id = [string]$identity.device_id }) `
        -TimeoutSec 12 | Out-Null
} catch {
    # O marcador local permanece para que um receptor presente possa repetir a operação.
}
