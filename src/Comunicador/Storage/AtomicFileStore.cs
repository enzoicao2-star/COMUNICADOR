using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Comunicador.Storage;

/// <summary>Writes local state files through unique temporary files and serializes
/// writers across Windows sessions that share the same user profile.</summary>
public static class AtomicFileStore
{
    public static void WriteAllText(string path, string contents)
    {
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096, leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
        });
    }

    public static void WriteAllBytes(string path, byte[] contents) =>
        Write(path, stream => stream.Write(contents));

    private static void Write(string path, Action<Stream> write)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The destination must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        using var mutex = new Mutex(false, MutexName(fullPath));
        var ownsMutex = false;
        var temporary = fullPath + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            try { ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) throw new IOException($"Timed out waiting to write local state: {fullPath}");

            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, bufferSize: 64 * 1024, FileOptions.WriteThrough))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            MoveWithRetry(temporary, fullPath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (ownsMutex) mutex.ReleaseMutex();
        }
    }

    private static string MutexName(string path)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return @"Global\Comunicador.AtomicFile." + Convert.ToHexString(digest);
    }

    private static void MoveWithRetry(string source, string destination)
    {
        const int attempts = 4;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < attempts - 1)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < attempts - 1)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }
}
