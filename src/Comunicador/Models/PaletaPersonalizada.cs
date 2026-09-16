using System.Text.Json.Serialization;

namespace Comunicador.Models;

public sealed class PaletaPersonalizada : ObservableModel
{
    private string _id = $"personalizada:{Guid.NewGuid():N}";
    private string _nome = "Minha cor";
    private string _cor = "#4C8DFF";
    private bool _selecionada;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string Nome { get => _nome; set => SetField(ref _nome, value); }
    public string Cor { get => _cor; set => SetField(ref _cor, value); }

    [JsonIgnore]
    public bool Selecionada
    {
        get => _selecionada;
        set
        {
            if (SetField(ref _selecionada, value)) OnPropertyChanged(nameof(EstadoSelecao));
        }
    }

    [JsonIgnore]
    public string EstadoSelecao => Selecionada ? "ativo" : string.Empty;
}
