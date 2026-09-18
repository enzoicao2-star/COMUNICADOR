param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\src\Comunicador\bin\Release\net10.0-windows\Comunicador.exe'),
    [int]$HoldSeconds = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ComunicadorWindowPosition {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
}
'@

$process = Start-Process -FilePath ([IO.Path]::GetFullPath($ExePath)) -PassThru
try {
    $limit = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $limit)
    if ($process.MainWindowHandle -eq 0) { throw 'A janela principal não abriu.' }

    $screens = @([System.Windows.Forms.Screen]::AllScreens)
    if ($screens.Count -gt 1) {
        $second = @($screens | Where-Object { -not $_.Primary })[0]
        $bounds = $second.WorkingArea
        $width = [Math]::Min(1280, $bounds.Width)
        $height = [Math]::Min(850, $bounds.Height)
        $x = $bounds.X + [Math]::Max(0, [int](($bounds.Width - $width) / 2))
        $y = $bounds.Y + [Math]::Max(0, [int](($bounds.Height - $height) / 2))
        [ComunicadorWindowPosition]::SetWindowPos(
            $process.MainWindowHandle, [IntPtr]::Zero, $x, $y, $width, $height, 0x0040) | Out-Null
        Start-Sleep -Milliseconds 500
    }

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    if ($null -eq $main) { throw 'A janela não apareceu na árvore de automação.' }

    function Invoke-Tab([string]$name) {
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $buttonCondition)
        $button = $null
        $buttonLimit = [DateTime]::UtcNow.AddSeconds(10)
        do {
            $button = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($null -eq $button) { Start-Sleep -Milliseconds 200 }
        } while ($null -eq $button -and [DateTime]::UtcNow -lt $buttonLimit)
        if ($null -eq $button) {
            $allButtons = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
            $buttonNames = @($allButtons | ForEach-Object { $_.Current.Name }) -join ', '
            throw "A aba '$name' não foi encontrada. Janela: '$($main.Current.Name)'. Botões: $buttonNames"
        }
        $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        Start-Sleep -Milliseconds 700
    }

    Invoke-Tab 'Mensagens'
    Invoke-Tab 'Lembretes'
    Invoke-Tab ("Hist" + [char]0x00F3 + "rico")
    Invoke-Tab 'Logs'
    Invoke-Tab ("Configura" + [char]0x00E7 + [char]0x00F5 + "es")
    Invoke-Tab 'Computadores'
    Invoke-Tab 'Mensagens'

    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition)
    if ($windows.Count -ne 1) {
        $names = @($windows | ForEach-Object { $_.Current.Name }) -join ', '
        throw "Foi aberta uma janela inesperada: $names"
    }

    Write-Host 'Interface validada: todas as abas alternaram sem erro.'
    if ($HoldSeconds -gt 0) { Start-Sleep -Seconds $HoldSeconds }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
