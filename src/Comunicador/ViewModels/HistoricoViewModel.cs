using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Comunicador.Services;
using Comunicador.Models;

namespace Comunicador.ViewModels;

public sealed class HistoricoViewModel : ViewModelBase
{
    private readonly HistoricoRepository _historico;
    private string _filtro = string.Empty;
    private string? _computadorSelecionadoId;

    public ObservableCollection<Models.HistoricoEntry> Itens => _historico.Itens;

    public ICollectionView ItensFiltrados { get; }
    public ObservableCollection<HistoricoComputadorFiltro> Computadores { get; } = new();

    public string? ComputadorSelecionadoId
    {
        get => _computadorSelecionadoId;
        set { if (SetField(ref _computadorSelecionadoId, value)) ItensFiltrados.Refresh(); }
    }

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
    public ICommand SelecionarComputadorCommand { get; }

    public HistoricoViewModel(HistoricoRepository historico)
    {
        _historico = historico;
        ItensFiltrados = CollectionViewSource.GetDefaultView(Itens);
        ItensFiltrados.Filter = FiltrarItem;
        Itens.CollectionChanged += (_, _) => AtualizarComputadores();
        LimparCommand = new RelayCommand(_ => { _historico.Limpar(); AtualizarComputadores(); });
        SelecionarComputadorCommand = new RelayCommand(param =>
            ComputadorSelecionadoId = param as string);
        AtualizarComputadores();
    }

    private bool FiltrarItem(object obj)
    {
        if (obj is not Models.HistoricoEntry entry)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ComputadorSelecionadoId)
            && entry.ComputadorId != ComputadorSelecionadoId) return false;
        if (string.IsNullOrWhiteSpace(Filtro)) return true;
        return entry.ComputadorNome.Contains(Filtro, StringComparison.OrdinalIgnoreCase)
            || entry.Titulo.Contains(Filtro, StringComparison.OrdinalIgnoreCase)
            || entry.Mensagem.Contains(Filtro, StringComparison.OrdinalIgnoreCase);
    }

    private void AtualizarComputadores()
    {
        var atual = ComputadorSelecionadoId;
        Computadores.Clear();
        foreach (var grupo in Itens.Where(i => !string.IsNullOrWhiteSpace(i.ComputadorId))
                     .GroupBy(i => i.ComputadorId)
                     .OrderBy(g => g.First().ComputadorNome))
        {
            Computadores.Add(new HistoricoComputadorFiltro(
                grupo.Key, grupo.First().ComputadorNome, grupo.Count()));
        }
        if (atual is not null && Computadores.All(c => c.Id != atual)) ComputadorSelecionadoId = null;
    }
}

public sealed record HistoricoComputadorFiltro(string Id, string Nome, int Quantidade)
{
    public string Rotulo => $"{Nome} ({Quantidade})";
}
