using System.IO;
using System.Text;
using Comunicador.Models;
using Comunicador.Storage;

namespace Comunicador.Services;

public static class Logger
{
    private static readonly object Lock = new();
    private static readonly Mutex ProcessLock = new(false, @"Local\Comunicador.LogFile");
    private static LogRepository? _repository;
    private static string _originId = Environment.MachineName;
    private static string _originName = Environment.MachineName;

    public static void Configure(LogRepository repository, string originId, string originName)
    {
        _repository = repository;
        _originId = originId;
        _originName = originName;
    }

    public static void Info(string message, string category = "geral") =>
        Write("INFO", NivelLog.Info, message, category, null);

    public static void Warning(string message, string category = "geral", string? details = null) =>
        Write("AVISO", NivelLog.Aviso, message, category, details);

    public static void Error(string message, string category = "geral", string? details = null) =>
        Write("ERRO", NivelLog.Erro, message, category, details);

    private static void Write(string level, NivelLog nivel, string message, string category, string? details)
    {
        try
        {
            lock (Lock)
            {
                AppPaths.EnsureCreated();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                var lockHeld = false;
                try
                {
                    try
                    {
                        lockHeld = ProcessLock.WaitOne(TimeSpan.FromSeconds(5));
                    }
                    catch (AbandonedMutexException)
                    {
                        lockHeld = true;
                    }

                    if (!lockHeld) return;
                    using var stream = new FileStream(AppPaths.LogFile, FileMode.Append,
                        FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.Write(line);
                    writer.Flush();
                }
                finally
                {
                    if (lockHeld) ProcessLock.ReleaseMutex();
                }
            }

            _repository?.Adicionar(new LogEntry
            {
                Nivel = nivel,
                TipoOrigem = "panel",
                OrigemId = _originId,
                OrigemNome = _originName,
                Categoria = category,
                Mensagem = message,
                Detalhes = details,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // logging nunca deve derrubar o app.
        }
    }
}
