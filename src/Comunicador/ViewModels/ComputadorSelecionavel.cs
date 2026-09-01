using Comunicador.Models;

namespace Comunicador.ViewModels;

public sealed class ComputadorSelecionavel : ViewModelBase
{
    private bool _selecionado;

    public Computador Computador { get; }

    public bool Selecionado
    {
        get => _selecionado;
        set => SetField(ref _selecionado, value);
    }

    public ComputadorSelecionavel(Computador computador)
    {
        Computador = computador;
    }
}
