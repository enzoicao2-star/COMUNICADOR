using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Comunicador.Services;

namespace Comunicador.ViewModels;

public sealed class HistoricoViewModel : ViewModelBase
{
    private readonly HistoricoRepository _historico;
    private string _filtro = string.Empty;

    public ObservableCollection<Models.HistoricoEntry> Itens => _historico.Itens;

    public ICollectionView ItensFiltrados { get; }

    public string Filtro
    {
        get => _filtro;
        set
        {
            if (SetField(ref _filtro, value))
            {
                ItensFiltrados.Refresh();
            }
        }
    }

    public ICommand LimparCommand { get; }

    public HistoricoViewModel(HistoricoRepository historico)
    {
        _historico = historico;
        ItensFiltrados = CollectionViewSource.GetDefaultView(Itens);
        ItensFiltrados.Filter = FiltrarItem;
        LimparCommand = new RelayCommand(_ => _historico.Limpar());
    }

    private bool FiltrarItem(object obj)
    {
        if (string.IsNullOrWhiteSpace(Filtro))
        {
            return true;
        }

        if (obj is not Models.HistoricoEntry entry)
        {
            return false;
        }

        return entry.ComputadorNome.Contains(Filtro, StringComparison.OrdinalIgnoreCase)
            || entry.Titulo.Contains(Filtro, StringComparison.OrdinalIgnoreCase)
            || entry.Mensagem.Contains(Filtro, StringComparison.OrdinalIgnoreCase);
    }
}
