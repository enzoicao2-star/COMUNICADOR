using System.IO;

namespace Comunicador.Storage;

public static class AppPaths
{
    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Comunicador");

    public static string ComputadoresFile => Path.Combine(RootDir, "computadores.json");
    public static string PaineisPareadosFile => Path.Combine(RootDir, "paineis_pareados.json");
    public static string LembretesFile => Path.Combine(RootDir, "lembretes.json");
    public static string HistoricoFile => Path.Combine(RootDir, "historico.json");
    public static string LogsFile => Path.Combine(RootDir, "logs.json");
    public static string PerfisComputadoresFile => Path.Combine(RootDir, "perfis_computadores.json");
    public static string ConfiguracoesFile => Path.Combine(RootDir, "config.json");
    public static string LogFile => Path.Combine(RootDir, "comunicador.log");
    public static string LocalSharedDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Comunicador");
    public static string DeviceIdentityFile => Path.Combine(LocalSharedDir, "device.json");
    public static string CloudSessionFile => Path.Combine(LocalSharedDir, "cloud_session.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(LocalSharedDir);
    }
}
