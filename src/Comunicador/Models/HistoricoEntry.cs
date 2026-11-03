namespace Comunicador.Models;

public enum StatusEnvio
{
    Enviando,
    Entregue,
    Exibido,
    Respondido,
    Erro,
    SemResposta,
    NaFila,
    SemConfirmacao,
    Removido,
    RecebidoPeloReceptor,
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
    private string? _cloudDeliveryId;
    private List<string> _persistentImageIds = [];
    private DateTime? _statusAtualizadoEm;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public DateTime Timestamp { get => _timestamp; set => SetField(ref _timestamp, value); }
    public DirecaoHistorico Direcao { get => _direcao; set => SetField(ref _direcao, value); }
    public string ComputadorId { get => _computadorId; set => SetField(ref _computadorId, value); }
    public string ComputadorNome { get => _computadorNome; set => SetField(ref _computadorNome, value); }
    public string Titulo { get => _titulo; set => SetField(ref _titulo, value); }
    public string Mensagem { get => _mensagem; set => SetField(ref _mensagem, value); }
    public StatusEnvio Status
    {
        get => _status;
        set { if (SetField(ref _status, value)) OnPropertyChanged(nameof(DetalheEntrega)); }
    }
    public string? RespostaTexto { get => _respostaTexto; set => SetField(ref _respostaTexto, value); }
    public string? ErroDetalhe { get => _erroDetalhe; set => SetField(ref _erroDetalhe, value); }
    public string? CloudDeliveryId { get => _cloudDeliveryId; set => SetField(ref _cloudDeliveryId, value); }
    public List<string> PersistentImageIds { get => _persistentImageIds; set => SetField(ref _persistentImageIds, value ?? []); }
    public DateTime? StatusAtualizadoEm { get => _statusAtualizadoEm; set => SetField(ref _statusAtualizadoEm, value); }

    [System.Text.Json.Serialization.JsonIgnore]
    public string DetalheEntrega => Status switch
    {
        StatusEnvio.NaFila => "Aguardando o receptor retirar este envio da fila.",
        StatusEnvio.RecebidoPeloReceptor => "O receptor retirou o envio da fila. A exibição ainda não foi confirmada.",
        StatusEnvio.Entregue => "O receptor recebeu o conteúdo. A exibição ainda não foi confirmada.",
        StatusEnvio.Exibido => "A exibição foi confirmada pelo receptor.",
        StatusEnvio.Respondido => "Uma resposta foi recebida.",
        StatusEnvio.SemConfirmacao => "A confirmação não chegou após 24 horas; o envio pode ainda estar pendente.",
        StatusEnvio.Erro => "A entrega falhou; veja o motivo abaixo.",
        StatusEnvio.Removido => "A remoção da mídia foi confirmada.",
        StatusEnvio.SemResposta => "A exibição foi confirmada, mas não houve resposta.",
        _ => "Preparando o envio.",
    };
}
