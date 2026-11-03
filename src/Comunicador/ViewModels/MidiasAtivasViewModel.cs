using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Comunicador.Models;

namespace Comunicador.ViewModels;

public sealed record MidiaAtivaItem(Computador Computador, ImagemPersistenteAtiva Imagem);

/// <summary>Reúne as mídias conhecidas localmente; o receptor confirma o estado ao atualizar.</summary>
public sealed class MidiasAtivasViewModel : ViewModelBase, IDisposable
{
    private readonly ComputadoresViewModel _computadores;
    private readonly HashSet<Computador> _observados = [];
    private string _statusOperacao = "Mostrando a última informação recebida. Atualize para consultar os receptores.";
    private bool _atualizando;

    public ObservableCollection<MidiaAtivaItem> Itens { get; } = [];
    public string StatusOperacao { get => _statusOperacao; private set => SetField(ref _statusOperacao, value); }
    public bool Atualizando { get => _atualizando; private set => SetField(ref _atualizando, value); }
    public bool TemMidias => Itens.Count > 0;
    public bool PodeControlar => _computadores.PodeEnviarMidias;
    public ICommand AtualizarCommand { get; }

    public MidiasAtivasViewModel(ComputadoresViewModel computadores)
    {
        _computadores = computadores;
        AtualizarCommand = new AsyncRelayCommand(_ => AtualizarAsync(), _ => !Atualizando);
        _computadores.Computadores.CollectionChanged += ComputadoresAlterados;
        Itens.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TemMidias));
        ReconciliarComputadores();
    }

    private void ComputadoresAlterados(object? sender, NotifyCollectionChangedEventArgs e) =>
        ReconciliarComputadores();

    private void ReconciliarComputadores()
    {
        var atuais = _computadores.Computadores.ToHashSet();
        foreach (var antigo in _observados.Where(item => !atuais.Contains(item)).ToList())
        {
            antigo.PropertyChanged -= ComputadorAlterado;
            _observados.Remove(antigo);
        }
        foreach (var novo in atuais.Where(item => !_observados.Contains(item)))
        {
            novo.PropertyChanged += ComputadorAlterado;
            _observados.Add(novo);
        }
        RecarregarItens();
    }

    private void ComputadorAlterado(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Computador.ImagensPersistentesAtivas)) RecarregarItens();
    }

    private void RecarregarItens()
    {
        var encontrados = _computadores.Computadores
            .SelectMany(computador => computador.ImagensPersistentesAtivas
                .Select(imagem => new MidiaAtivaItem(computador, imagem)))
            .OrderBy(item => item.Computador.NomeExibicao)
            .ThenBy(item => item.Imagem.MonitorIndex)
            .ToList();
        Itens.Clear();
        foreach (var item in encontrados) Itens.Add(item);
    }

    public async Task AtualizarAsync()
    {
        if (Atualizando) return;
        Atualizando = true;
        try
        {
            var consultados = 0;
            var falhas = 0;
            foreach (var computador in _computadores.Computadores.Where(c => c.RegistradoNaNuvem && !c.EhOwner).ToList())
            {
                if (await _computadores.ConsultarMidiasAtivasAsync(computador)) consultados++;
                else falhas++;
            }
            StatusOperacao = consultados == 0 && falhas == 0
                ? "Nenhum receptor registrado na nuvem para consultar."
                : $"Consulta solicitada a {consultados} receptor(es); falhou em {falhas}. "
                    + "As prévias aparecem quando eles responderem.";
        }
        catch (Exception ex)
        {
            StatusOperacao = $"Não foi possível concluir a consulta: {ex.Message}";
        }
        finally
        {
            Atualizando = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task ControlarAsync(ImagemPersistenteAtiva imagem, string acao,
        double? xAnterior = null, double? yAnterior = null)
    {
        if (!PodeControlar)
        {
            StatusOperacao = "Este painel não tem permissão para controlar mídias.";
            return;
        }
        await _computadores.ControlarImagemPersistenteAsync(imagem, acao);
        if (xAnterior.HasValue && yAnterior.HasValue
            && _computadores.StatusMensagem?.StartsWith("Posição da imagem atualizada",
                StringComparison.OrdinalIgnoreCase) != true)
        {
            imagem.XPercent = xAnterior.Value;
            imagem.YPercent = yAnterior.Value;
        }
        StatusOperacao = _computadores.StatusMensagem ?? string.Empty;
    }

    public void Dispose()
    {
        _computadores.Computadores.CollectionChanged -= ComputadoresAlterados;
        foreach (var computador in _observados) computador.PropertyChanged -= ComputadorAlterado;
        _observados.Clear();
    }

    public void AtualizarTemposRestantes()
    {
        foreach (var item in Itens) item.Imagem.AtualizarTempoRestante();
    }
}
