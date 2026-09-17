using System.Text.Json.Serialization;

namespace Comunicador.Models;

/// <summary>Badge visual atribuída a um computador. O modelo é serializado tanto
/// no armazenamento local quanto na sincronização entre painéis.</summary>
public sealed class BadgeUsuario : ObservableModel
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _texto = "OWNER";
    private string _cor = "#F3C969";
    private string _estilo = "Holográfica";
    private string _icone = "Coroa";
    private string? _iconePersonalizadoBase64;
    private bool _brilho = true;
    private bool _efeitoMouse = true;
    private string? _computerId;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string Texto { get => _texto; set => SetField(ref _texto, value); }
    public string Cor { get => _cor; set => SetField(ref _cor, value); }
    public string Estilo { get => _estilo; set => SetField(ref _estilo, value); }
    public string Icone { get => _icone; set => SetField(ref _icone, value); }
    public string? IconePersonalizadoBase64
    {
        get => _iconePersonalizadoBase64;
        set => SetField(ref _iconePersonalizadoBase64, value);
    }
    public bool Brilho { get => _brilho; set => SetField(ref _brilho, value); }
    public bool EfeitoMouse { get => _efeitoMouse; set => SetField(ref _efeitoMouse, value); }

    [JsonIgnore]
    public string? ComputerId { get => _computerId; set => SetField(ref _computerId, value); }

    [JsonIgnore]
    public bool TemIconePersonalizado => !string.IsNullOrWhiteSpace(IconePersonalizadoBase64);

    public BadgeUsuario Clone() => new()
    {
        Id = Id,
        Texto = Texto,
        Cor = Cor,
        Estilo = Estilo,
        Icone = Icone,
        IconePersonalizadoBase64 = IconePersonalizadoBase64,
        Brilho = Brilho,
        EfeitoMouse = EfeitoMouse,
    };
}
