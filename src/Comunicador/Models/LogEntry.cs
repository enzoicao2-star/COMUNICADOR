namespace Comunicador.Models;

public enum NivelLog
{
    Info,
    Aviso,
    Erro,
}

public sealed class LogEntry : ObservableModel
{
    private string _id = Guid.NewGuid().ToString();
    private DateTime _timestampUtc = DateTime.UtcNow;
    private NivelLog _nivel;
    private string _tipoOrigem = "panel";
    private string _origemId = string.Empty;
    private string _origemNome = string.Empty;
    private string _categoria = "geral";
    private string _mensagem = string.Empty;
    private string? _detalhes;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public DateTime TimestampUtc { get => _timestampUtc; set => SetField(ref _timestampUtc, value); }
    public NivelLog Nivel { get => _nivel; set => SetField(ref _nivel, value); }
    public string TipoOrigem { get => _tipoOrigem; set => SetField(ref _tipoOrigem, value); }
    public string OrigemId { get => _origemId; set => SetField(ref _origemId, value); }
    public string OrigemNome { get => _origemNome; set => SetField(ref _origemNome, value); }
    public string Categoria { get => _categoria; set => SetField(ref _categoria, value); }
    public string Mensagem { get => _mensagem; set => SetField(ref _mensagem, value); }
    public string? Detalhes { get => _detalhes; set => SetField(ref _detalhes, value); }
}
