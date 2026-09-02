using Comunicador.Protocol;

namespace Comunicador.ViewModels;

/// <summary>Botão de resposta enquanto está sendo montado na tela de Mensagens.</summary>
public sealed class BotaoRespostaEditavel : ViewModelBase
{
    private string _rotulo = string.Empty;
    private string? _url;

    public string Rotulo
    {
        get => _rotulo;
        set => SetField(ref _rotulo, value);
    }

    public string? Url
    {
        get => _url;
        set
        {
            if (SetField(ref _url, value))
            {
                OnPropertyChanged(nameof(TemLink));
                OnPropertyChanged(nameof(Descricao));
            }
        }
    }

    public bool TemLink => !string.IsNullOrWhiteSpace(Url);

    public string Descricao => TemLink ? Url! : "responde com o texto do botão";

    public BotaoResposta ParaProtocolo() => new()
    {
        Label = Rotulo,
        Url = string.IsNullOrWhiteSpace(Url) ? null : Url,
    };
}
