param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\src\Comunicador\bin\Release\net10.0-windows\Comunicador.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Start-Process -FilePath ([IO.Path]::GetFullPath($ExePath)) -PassThru
try {
    $limit = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $limit)
    if ($process.MainWindowHandle -eq 0) { throw 'A janela principal não abriu.' }

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $main = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $processCondition)
    if ($null -eq $main) { throw 'A janela não apareceu na árvore de automação.' }

    function Invoke-Tab([string]$name) {
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $buttonCondition)
        $button = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $button) { throw "A aba '$name' não foi encontrada." }
        $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        Start-Sleep -Milliseconds 700
    }

    Invoke-Tab 'Mensagens'
    Invoke-Tab 'Logs'
    Invoke-Tab 'Computadores'
    Invoke-Tab 'Mensagens'

    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition)
    if ($windows.Count -ne 1) {
        $names = @($windows | ForEach-Object { $_.Current.Name }) -join ', '
        throw "Foi aberta uma janela inesperada: $names"
    }

    Write-Host 'Interface validada: Mensagens, Logs e Computadores alternaram sem erro.'
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
