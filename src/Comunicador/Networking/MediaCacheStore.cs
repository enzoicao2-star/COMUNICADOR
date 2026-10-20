using System.IO;
using System.Security.Cryptography;
using Comunicador.Protocol;

namespace Comunicador.Networking;

/// <summary>Local media prepared for a later notification to this panel.</summary>
internal sealed class MediaCacheStore
{
    private const long MaxStoredBytes = 256L * 1024 * 1024;
    private readonly string _directory;

    public MediaCacheStore(string directory) => _directory = directory;

    public async Task StoreAsync(string digest, string kind, string mimeType, byte[] data,
        bool testOnly, CancellationToken ct)
    {
        if (!ValidDigest(digest) || !HashMatches(digest, data))
            throw new InvalidDataException("A mídia não corresponde ao identificador informado.");
        if (testOnly) return;
        if (!ValidMedia(kind, mimeType, data))
            throw new InvalidDataException("O conteúdo não corresponde ao tipo de mídia informado.");

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, digest.ToLowerInvariant());
        if (File.Exists(path))
        {
            try
            {
                var existing = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                if (HashMatches(digest, existing) && ValidMedia(kind, mimeType, existing))
                {
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                    return;
                }
            }
            catch (IOException) { /* Replace an incomplete cache file. */ }
            catch (UnauthorizedAccessException) { /* The replacement will report the error. */ }
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(data, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Prune(path);
    }

    public bool TryResolve(ComunicadorMessage message)
    {
        if (message.Image is { } image && !ResolveImage(image)) return false;
        if (message.ScreenImages is { } images)
            foreach (var item in images)
                if (!ResolveImage(item.Image)) return false;
        if (message.Video is { } video && !ResolveVideo(video)) return false;
        if (message.ScreenVideos is { } videos)
            foreach (var item in videos)
                if (!ResolveVideo(item.Video)) return false;
        if (message.Audio is { } audio && !ResolveAudio(audio)) return false;
        return true;
    }

    public static bool HasReferences(ComunicadorMessage message) =>
        message.Image?.MediaCacheId is not null
        || message.ScreenImages?.Any(item => item.Image.MediaCacheId is not null) == true
        || message.Video?.MediaCacheId is not null
        || message.ScreenVideos?.Any(item => item.Video.MediaCacheId is not null) == true
        || message.Audio?.MediaCacheId is not null;

    private bool ResolveImage(ConteudoImagem image)
    {
        if (image.MediaCacheId is null) return true;
        if (!TryLoad(image.MediaCacheId, "image", image.MimeType, out var data)) return false;
        image.DataBase64 = Convert.ToBase64String(data);
        image.MediaCacheId = null;
        return true;
    }

    private bool ResolveVideo(ConteudoVideo video)
    {
        if (video.MediaCacheId is null) return true;
        if (!TryLoad(video.MediaCacheId, "video", video.MimeType, out var data)) return false;
        video.DataBase64 = Convert.ToBase64String(data);
        video.MediaCacheId = null;
        return true;
    }

    private bool ResolveAudio(ConteudoAudio audio)
    {
        if (audio.MediaCacheId is null) return true;
        if (!TryLoad(audio.MediaCacheId, "audio", audio.MimeType, out var data)) return false;
        audio.DataBase64 = Convert.ToBase64String(data);
        audio.MediaCacheId = null;
        return true;
    }

    private bool TryLoad(string digest, string kind, string mimeType, out byte[] data)
    {
        data = [];
        if (!ValidDigest(digest)) return false;
        try
        {
            var path = Path.Combine(_directory, digest.ToLowerInvariant());
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxBytes(kind, mimeType)) return false;
            data = File.ReadAllBytes(path);
            return HashMatches(digest, data) && ValidMedia(kind, mimeType, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool ValidMedia(string kind, string mimeType, byte[] data) =>
        data.Length > 0 && data.Length <= MaxBytes(kind, mimeType) && (kind switch
        {
            "image" or "gif" => ConteudoImagem.MimePermitido(mimeType)
                && (kind != "gif" || mimeType == "image/gif")
                && ConteudoImagem.AssinaturaCorresponde(mimeType, data),
            "video" => ConteudoVideo.MimePermitido(mimeType)
                && ConteudoVideo.AssinaturaCorresponde(mimeType, data),
            "audio" => ConteudoAudio.MimePermitido(mimeType)
                && ConteudoAudio.AssinaturaCorresponde(mimeType, data),
            _ => false,
        });

    private static int MaxBytes(string kind, string mimeType) => kind switch
    {
        "image" or "gif" => ProtocolConstants.MaxImageBytesForMime(mimeType),
        "video" => ProtocolConstants.MaxVideoBytes,
        "audio" => ProtocolConstants.MaxAudioBytes,
        _ => 0,
    };

    private static bool ValidDigest(string digest) =>
        digest.Length == 64 && digest.All(Uri.IsHexDigit);

    private static bool HashMatches(string digest, byte[] data) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(data)), digest,
            StringComparison.OrdinalIgnoreCase);

    private void Prune(string currentPath)
    {
        try
        {
            var files = new DirectoryInfo(_directory).EnumerateFiles()
                .Where(file => ValidDigest(file.Name))
                .OrderBy(file => file.LastWriteTimeUtc).ToList();
            var total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= MaxStoredBytes) break;
                if (string.Equals(file.FullName, currentPath, StringComparison.OrdinalIgnoreCase)) continue;
                try { var length = file.Length; file.Delete(); total -= length; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
