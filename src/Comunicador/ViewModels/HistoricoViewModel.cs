using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using Comunicador.Networking;
using Comunicador.Services;
using Comunicador.Models;

namespace Comunicador.ViewModels;

public sealed class HistoricoViewModel : ViewModelBase
{
    private readonly HistoricoRepository _historico;
    private readonly ReenvioRepository _reenvios;
    private readonly ComputadoresViewModel _computadores;
    private readonly EnviadorNotificacoes _enviador;
    private string _filtro = string.Empty;
    private string? _computadorSelecionadoId;
    private string _statusSelecionado = string.Empty;
    private string _statusOperacao = string.Empty;

    public ObservableCollection<Models.HistoricoEntry> Itens => _historico.Itens;

    public ICollectionView ItensFiltrados { get; }
    public ObservableCollection<HistoricoComputadorFiltro> Computadores { get; } = new();
    public ObservableCollection<ResumoEntrega> StatusFiltros { get; } = new();

    public string StatusOperacao { get => _statusOperacao; private set => SetField(ref _statusOperacao, value); }
    public string StatusSelecionado
    {
        get => _statusSelecionado;
        set { if (SetField(ref _statusSelecionado, value)) ItensFiltrados.Refresh(); }
    }

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
    public ICommand SelecionarStatusCommand { get; }
    public ICommand ReenviarCommand { get; }

    public HistoricoViewModel(HistoricoRepository historico, ReenvioRepository reenvios,
        ComputadoresViewModel computadores, EnviadorNotificacoes enviador)
    {
        _historico = historico;
        _reenvios = reenvios;
        _computadores = computadores;
        _enviador = enviador;
        ItensFiltrados = CollectionViewSource.GetDefaultView(Itens);
        ItensFiltrados.Filter = FiltrarItem;
        _historico.Changed += () => { AtualizarComputadores(); AtualizarResumo(); CommandManager.InvalidateRequerySuggested(); };
        LimparCommand = new RelayCommand(_ =>
        {
            foreach (var item in Itens.ToList()) _reenvios.Remover(item.Id);
            _historico.Limpar();
        });
        SelecionarComputadorCommand = new RelayCommand(param =>
            ComputadorSelecionadoId = param as string);
        SelecionarStatusCommand = new RelayCommand(param => StatusSelecionado = param as string ?? string.Empty);
        ReenviarCommand = new AsyncRelayCommand(param => ReenviarAsync((Models.HistoricoEntry)param!),
            param => param is Models.HistoricoEntry entry && entry.Status == StatusEnvio.Erro
                && _reenvios.Existe(entry.Id));
        AtualizarComputadores();
        AtualizarResumo();
    }

    private bool FiltrarItem(object obj)
    {
        if (obj is not Models.HistoricoEntry entry)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ComputadorSelecionadoId)
            && entry.ComputadorId != ComputadorSelecionadoId) return false;
        if (!string.IsNullOrWhiteSpace(StatusSelecionado)
            && (!Enum.TryParse<StatusEnvio>(StatusSelecionado, out var selecionado)
                || entry.Status != selecionado)) return false;
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

    private void AtualizarResumo()
    {
        StatusFiltros.Clear();
        StatusFiltros.Add(new ResumoEntrega(string.Empty, $"Todos ({Itens.Count})"));
        foreach (var status in Enum.GetValues<StatusEnvio>())
        {
            var count = Itens.Count(i => i.Status == status);
            StatusFiltros.Add(new ResumoEntrega(status.ToString(), $"{status} ({count})"));
        }
        ItensFiltrados.Refresh();
    }

    private async Task ReenviarAsync(Models.HistoricoEntry entry)
    {
        Models.HistoricoEntry? tentativa = null;
        EnvioPendente? envio = null;
        try
        {
            envio = _reenvios.Ler(entry.Id);
            var computador = _computadores.Computadores.FirstOrDefault(c => c.Id == entry.ComputadorId);
            if (envio is null || computador is null || !computador.Pareado)
            {
                StatusOperacao = "Reenvio indisponível: o conteúdo original ou o pareamento não está neste painel.";
                return;
            }
            StatusOperacao = $"Reenviando para {computador.NomeExibicao}…";
            tentativa = new Models.HistoricoEntry
            {
                ComputadorId = computador.Id,
                ComputadorNome = computador.NomeExibicao,
                Titulo = entry.Titulo,
                Mensagem = entry.Mensagem,
                Status = StatusEnvio.Enviando,
            };
            _historico.Adicionar(tentativa);
            var resultado = await envio.EnviarAsync(_enviador, computador).ConfigureAwait(true);
            _historico.AtualizarExistente(tentativa.Id,
                item => EnvioPendente.AtualizarHistorico(item, resultado, envio.PermitirResposta));
            try
            {
                if (!resultado.Delivered) _reenvios.Salvar(tentativa.Id, envio);
                _reenvios.Remover(entry.Id);
            }
            catch (IOException ex)
            {
                Logger.Warning($"Não foi possível atualizar o reenvio salvo: {ex.Message}");
            }
            CommandManager.InvalidateRequerySuggested();
            if (resultado.Delivered)
            {
                StatusOperacao = $"Envio entregue a {computador.NomeExibicao}.";
                if (resultado.GotReply)
                    _ = Views.NotificacaoRecebidaWindow.MostrarAsync(computador.NomeExibicao,
                        "Resposta recebida", resultado.ReplyText ?? "O usuário confirmou o recebimento.",
                        allowReply: false);
            }
            else StatusOperacao = $"Falha ao reenviar: {resultado.ErrorMessage}";
        }
        catch (Exception ex)
        {
            if (tentativa is not null)
            {
                _historico.AtualizarExistente(tentativa.Id, item =>
                {
                    item.Status = StatusEnvio.Erro;
                    item.ErroDetalhe = ex.Message;
                });
                try
                {
                    if (envio is not null) _reenvios.Salvar(tentativa.Id, envio);
                    _reenvios.Remover(entry.Id);
                }
                catch (IOException saveEx)
                {
                    Logger.Warning($"Não foi possível atualizar o reenvio salvo: {saveEx.Message}");
                }
                CommandManager.InvalidateRequerySuggested();
            }
            StatusOperacao = $"Falha ao reenviar: {ex.Message}";
        }
    }
}

public sealed record ResumoEntrega(string Chave, string Rotulo);

public sealed record HistoricoComputadorFiltro(string Id, string Nome, int Quantidade)
{
    public string Rotulo => $"{Nome} ({Quantidade})";
}
