using System.IO;
using Comunicador.Storage;

namespace Comunicador.Services;

public static class Logger
{
    private static readonly object Lock = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERRO", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                AppPaths.EnsureCreated();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(AppPaths.LogFile, line);
            }
        }
        catch (IOException)
        {
            // logging nunca deve derrubar o app.
        }
    }
}
