using System.IO;
using System.Text.Json;
using Comunicador.Models;
using Comunicador.Storage;

namespace Comunicador.Services;

/// <summary>Guarda apenas envios que falharam, com mídia, para reenvio fiel no painel de origem.</summary>
public sealed class ReenvioRepository
{
    private readonly string _directory;

    public ReenvioRepository(string? directory = null) => _directory = directory ?? AppPaths.ReenviosDir;

    private string? PathFor(string id) => Guid.TryParse(id, out var parsed)
        ? Path.Combine(_directory, parsed.ToString("D") + ".json") : null;

    public bool Existe(string id) => PathFor(id) is { } path && File.Exists(path);

    public void Salvar(string id, EnvioPendente envio)
    {
        var path = PathFor(id) ?? throw new ArgumentException("Identificador de envio inválido.", nameof(id));
        Directory.CreateDirectory(_directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(envio));
        File.Move(temp, path, true);
    }

    public EnvioPendente? Ler(string id)
    {
        var path = PathFor(id);
        if (path is null || !File.Exists(path)) return null;
        return JsonSerializer.Deserialize<EnvioPendente>(File.ReadAllText(path));
    }

    public void Remover(string id)
    {
        var path = PathFor(id);
        if (path is not null && File.Exists(path)) File.Delete(path);
    }
}
