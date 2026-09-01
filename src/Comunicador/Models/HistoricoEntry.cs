namespace Comunicador.Models;

public enum StatusEnvio
{
    Enviando,
    Entregue,
    Exibido,
    Respondido,
    Erro,
    SemResposta,
}

public enum DirecaoHistorico
{
    Enviada,
    Recebida,
}

public sealed class HistoricoEntry : ObservableModel
{
    private string _id = Guid.NewGuid().ToString();
    private DateTime _timestamp = DateTime.Now;
    private DirecaoHistorico _direcao = DirecaoHistorico.Enviada;
    private string _computadorId = string.Empty;
    private string _computadorNome = string.Empty;
    private string _titulo = string.Empty;
    private string _mensagem = string.Empty;
    private StatusEnvio _status = StatusEnvio.Enviando;
    private string? _respostaTexto;
    private string? _erroDetalhe;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public DateTime Timestamp { get => _timestamp; set => SetField(ref _timestamp, value); }
    public DirecaoHistorico Direcao { get => _direcao; set => SetField(ref _direcao, value); }
    public string ComputadorId { get => _computadorId; set => SetField(ref _computadorId, value); }
    public string ComputadorNome { get => _computadorNome; set => SetField(ref _computadorNome, value); }
    public string Titulo { get => _titulo; set => SetField(ref _titulo, value); }
    public string Mensagem { get => _mensagem; set => SetField(ref _mensagem, value); }
    public StatusEnvio Status { get => _status; set => SetField(ref _status, value); }
    public string? RespostaTexto { get => _respostaTexto; set => SetField(ref _respostaTexto, value); }
    public string? ErroDetalhe { get => _erroDetalhe; set => SetField(ref _erroDetalhe, value); }
}
