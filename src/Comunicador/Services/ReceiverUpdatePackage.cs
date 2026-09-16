using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Comunicador.Protocol;

namespace Comunicador.Services;

/// <summary>Lê a cópia oficial do receptor incorporada na própria compilação do painel.
/// A atualização funciona pela rede local e não recebe URL ou caminho executável.</summary>
public static class ReceiverUpdatePackage
{
    private static readonly string[] FileNames = ["receptor.py", "protocolo.py"];

    public static List<ArquivoAtualizacao> Create()
    {
        var assembly = typeof(ReceiverUpdatePackage).Assembly;
        return FileNames.Select(nome => CreateFile(assembly, nome)).ToList();
    }

    private static ArquivoAtualizacao CreateFile(Assembly assembly, string nome)
    {
        var resourceName = $"Comunicador.Receiver.{nome}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso incorporado ausente: {resourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        return new ArquivoAtualizacao
        {
            Name = nome,
            ContentBase64 = Convert.ToBase64String(bytes),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        };
    }
}
