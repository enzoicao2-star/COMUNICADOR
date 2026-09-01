namespace Comunicador.Models;

/// <summary>Um painel remoto que se pareou com ESTE Comunicador (ou seja, este
/// computador agindo como receptor de outro painel). Espelha, do lado C#, o que
/// receptor.py guarda em config["paired_panels"].</summary>
public sealed class PainelPareado : ObservableModel
{
    private string _panelId = string.Empty;
    private string _panelName = string.Empty;
    private string _token = string.Empty;
    private DateTime _pareadoEm = DateTime.UtcNow;

    public string PanelId { get => _panelId; set => SetField(ref _panelId, value); }
    public string PanelName { get => _panelName; set => SetField(ref _panelName, value); }
    public string Token { get => _token; set => SetField(ref _token, value); }
    public DateTime PareadoEm { get => _pareadoEm; set => SetField(ref _pareadoEm, value); }
}
