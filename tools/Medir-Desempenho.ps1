param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\src\Comunicador\bin\Release\net10.0-windows\Comunicador.exe'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\performance.json'),
    [int]$Runs = 3
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ComunicadorBenchmarkWindow {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
'@

$exe = [IO.Path]::GetFullPath($ExePath)
$output = [IO.Path]::GetFullPath($OutputPath)
$results = @()
for ($run = 1; $run -le $Runs; $run++) {
    $runFile = [IO.Path]::ChangeExtension($output, ".run$run.json")
    Remove-Item -LiteralPath $runFile -Force -ErrorAction SilentlyContinue
    $started = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $exe -ArgumentList ('--benchmark-file=' + $runFile) -PassThru
    $limit = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 25
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $limit)
    $visibleMs = $started.Elapsed.TotalMilliseconds
    if ($process.MainWindowHandle -eq 0) { throw "A janela não abriu na medição $run." }

    $screens = @([System.Windows.Forms.Screen]::AllScreens)
    if ($screens.Count -gt 1) {
        $screen = @($screens | Where-Object { -not $_.Primary })[0].WorkingArea
        [ComunicadorBenchmarkWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero,
            $screen.X + 20, $screen.Y + 20, [Math]::Min(1260, $screen.Width - 40),
            [Math]::Min(820, $screen.Height - 40), 0x0040) | Out-Null
    }
    if (-not $process.WaitForExit(45000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "A medição $run não terminou."
    }
    if (-not (Test-Path -LiteralPath $runFile)) { throw "O resultado da medição $run não foi criado." }
    $item = Get-Content -LiteralPath $runFile -Raw | ConvertFrom-Json
    $item | Add-Member -NotePropertyName process_to_window_ms -NotePropertyValue ([Math]::Round($visibleMs, 2))
    $results += $item
}

$summary = [ordered]@{
    measured_at = [DateTimeOffset]::Now.ToString('o')
    runs = $Runs
    process_to_window_ms = [Math]::Round(($results.process_to_window_ms | Measure-Object -Average).Average, 2)
    window_ready_ms = [Math]::Round(($results.window_ready_ms | Measure-Object -Average).Average, 2)
    tab_average_ms = [Math]::Round(($results.tab_average_ms | Measure-Object -Average).Average, 2)
    tab_p95_ms = [Math]::Round(($results.tab_p95_ms | Measure-Object -Average).Average, 2)
    tab_max_ms = [Math]::Round(($results.tab_max_ms | Measure-Object -Maximum).Maximum, 2)
    working_set_mb = [Math]::Round(($results.working_set_mb | Measure-Object -Average).Average, 2)
    cpu_ms = [Math]::Round(($results.cpu_ms | Measure-Object -Average).Average, 2)
    hide_transition_cpu_ms = [Math]::Round(($results.hide_transition_cpu_ms | Measure-Object -Average).Average, 2)
    hidden_cpu_ms = [Math]::Round(($results.hidden_cpu_ms | Measure-Object -Average).Average, 2)
    details = $results
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $output -Encoding UTF8
$results | ForEach-Object { Remove-Item -LiteralPath ([IO.Path]::ChangeExtension($output, ".run$([array]::IndexOf($results, $_) + 1).json")) -Force -ErrorAction SilentlyContinue }
$summary | Format-List
