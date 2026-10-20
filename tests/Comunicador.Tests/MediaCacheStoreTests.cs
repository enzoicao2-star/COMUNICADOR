using System.IO;
using System.Security.Cryptography;
using Comunicador.Networking;
using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public sealed class MediaCacheStoreTests
{
    [Fact]
    public async Task TesteDescartaAmostraEMidiaRealSoFicaProntaDepoisDaGravacaoCompleta()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MediaCacheStore(directory);
            var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
            var digest = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var notification = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
            notification.Image = new ConteudoImagem
            {
                Name = "amostra.jpg", MimeType = "image/jpeg", MediaCacheId = digest,
            };

            await store.StoreAsync(digest, "image", "image/jpeg", data, testOnly: true, CancellationToken.None);
            Assert.False(Directory.Exists(directory));
            Assert.False(store.TryResolve(notification));

            await store.StoreAsync(digest, "image", "image/jpeg", data, testOnly: false, CancellationToken.None);
            Assert.True(store.TryResolve(notification));
            Assert.Null(notification.Image.MediaCacheId);
            Assert.Equal(data, Convert.FromBase64String(notification.Image.DataBase64));

            notification.Image.MediaCacheId = digest;
            notification.Image.DataBase64 = string.Empty;
            File.WriteAllBytes(Path.Combine(directory, digest), [0xFF, 0xD8, 0xFF]);
            Assert.False(store.TryResolve(notification));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
