namespace Comunicador.Models;

public sealed class Lembrete : ObservableModel
{
    private string _id = Guid.NewGuid().ToString();
    private string _titulo = string.Empty;
    private string _mensagem = string.Empty;
    private List<string> _computadorIds = new();
    private DateTime _dataHora;
    private bool _permitirResposta = true;
    private bool _enviado;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string Titulo { get => _titulo; set => SetField(ref _titulo, value); }
    public string Mensagem { get => _mensagem; set => SetField(ref _mensagem, value); }
    public List<string> ComputadorIds { get => _computadorIds; set => SetField(ref _computadorIds, value); }
    public DateTime DataHora { get => _dataHora; set => SetField(ref _dataHora, value); }
    public bool PermitirResposta { get => _permitirResposta; set => SetField(ref _permitirResposta, value); }
    public bool Enviado { get => _enviado; set => SetField(ref _enviado, value); }
}
