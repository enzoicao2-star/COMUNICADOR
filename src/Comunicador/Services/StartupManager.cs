using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Comunicador.Services;

/// <summary>Registers/unregisters the panel in the standard per-user Run key so autostart is
/// visible in Task Manager's Startup tab and easy to disable there — no hidden persistence.</summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
#if TEST_BUILD
    private const string ValueName = "Comunicador Teste";
#else
    private const string ValueName = "Comunicador";
#endif

    public static void Aplicar(bool habilitar)
    {
        try
        {
            if (habilitar)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath)) return;
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                key?.SetValue(ValueName, $"\"{exePath}\" --background");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Não foi possível atualizar a inicialização do Comunicador: {ex.Message}");
        }
    }

    public static bool EstaHabilitado()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }
}
