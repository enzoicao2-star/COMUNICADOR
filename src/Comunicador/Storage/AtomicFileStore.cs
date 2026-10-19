using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

    public static void WriteAllTextWithRecovery(string path, string contents)
    {
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096, leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
        }, recoverLockedDestination: true);
    }

    public static string? ReadAllTextWithRecovery(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var recoveryPath = fullPath + ".recovery";
        var candidates = new[] { fullPath, recoveryPath }
            .Concat(SnapshotPaths(fullPath))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(candidate => candidate == fullPath ? 0 : candidate == recoveryPath ? 1 : 2)
            .ToList();
        if (candidates.Count == 0) return null;

        Exception? firstError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                using var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var contents = reader.ReadToEnd();
                using var document = JsonDocument.Parse(contents);
                if (document.RootElement.ValueKind == JsonValueKind.Object) return contents;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                firstError ??= ex;
            }
        }

        throw new IOException($"Could not read local state from '{fullPath}' or its recovery copy.", firstError);
    }

    public static void WriteAllBytes(string path, byte[] contents) =>
        Write(path, stream => stream.Write(contents));

    private static void Write(string path, Action<Stream> write, bool recoverLockedDestination = false)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The destination must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        using var mutex = new Mutex(false, MutexName(fullPath));
        var ownsMutex = false;
        var retainedSnapshot = false;
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

            if (recoverLockedDestination)
            {
                try
                {
                    MoveWithRetry(temporary, fullPath);
                    TryDelete(fullPath + ".recovery");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    try { MoveWithRetry(temporary, fullPath + ".recovery"); }
                    catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException)
                    {
                        // O arquivo já foi gravado e fechado. Mantê-lo com nome exclusivo
                        // preserva os dados mesmo quando o Windows bloqueia renomeações.
                        retainedSnapshot = true;
                    }
                }
            }
            else
            {
                MoveWithRetry(temporary, fullPath);
            }
            if (recoverLockedDestination) PruneSnapshots(fullPath);
        }
        finally
        {
            if (!retainedSnapshot) TryDelete(temporary);
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

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IEnumerable<string> SnapshotPaths(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory)) return [];
        var fileName = Path.GetFileName(fullPath);
        var prefix = fileName + ".";
        return Directory.EnumerateFiles(directory, fileName + ".*.tmp")
            .Where(candidate =>
            {
                var name = Path.GetFileName(candidate);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
                var parts = name[prefix.Length..].Split('.');
                return parts.Length == 3 && int.TryParse(parts[0], out _)
                    && Guid.TryParseExact(parts[1], "N", out _)
                    && parts[2].Equals("tmp", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static void PruneSnapshots(string fullPath)
    {
        try
        {
            foreach (var obsolete in SnapshotPaths(fullPath)
                .OrderByDescending(File.GetLastWriteTimeUtc).Skip(3))
                TryDelete(obsolete);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
