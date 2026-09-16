using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Services;
using Microsoft.Win32;

namespace Comunicador.ViewModels;

public sealed class LogsViewModel : ViewModelBase
{
    private readonly LogRepository _repository;
    private readonly SyncCoordinatorService _sync;
    private string _filtro = string.Empty;
    private string _nivel = "Todos";
    private string _origem = "Todas";
    private string? _status;

    public ObservableCollection<LogEntry> Itens => _repository.Itens;
    public ICollectionView ItensFiltrados { get; }
    public IReadOnlyList<string> Niveis { get; } = ["Todos", "Erros", "Avisos", "Informações"];
    public IReadOnlyList<string> Origens { get; } = ["Todas", "Painéis", "Receptores"];

    public string Filtro
    {
        get => _filtro;
        set { if (SetField(ref _filtro, value)) ItensFiltrados.Refresh(); }
    }

    public string Nivel
    {
        get => _nivel;
        set { if (SetField(ref _nivel, value)) ItensFiltrados.Refresh(); }
    }

    public string Origem
    {
        get => _origem;
        set { if (SetField(ref _origem, value)) ItensFiltrados.Refresh(); }
    }

    public string? Status { get => _status; set => SetField(ref _status, value); }

    public ICommand AtualizarCommand { get; }
    public ICommand ExportarCommand { get; }
    public ICommand LimparCommand { get; }

    public LogsViewModel(LogRepository repository, SyncCoordinatorService sync)
    {
        _repository = repository;
        _sync = sync;
        ItensFiltrados = CollectionViewSource.GetDefaultView(Itens);
        ItensFiltrados.Filter = Filtrar;
        AtualizarCommand = new AsyncRelayCommand(AtualizarAsync);
        ExportarCommand = new RelayCommand(_ => Exportar());
        LimparCommand = new RelayCommand(_ =>
        {
            _repository.Limpar();
            Status = "Log consolidado local limpo.";
        });
    }

    private async Task AtualizarAsync()
    {
        Status = "Coletando logs dos receptores e sincronizando painéis...";
        var result = await _sync.SyncNowAsync().ConfigureAwait(true);
        ItensFiltrados.Refresh();
        Status = $"{result.ComputersReached} máquina(s) consultada(s), "
            + $"{result.LogsAdded} registro(s) novo(s) e {result.HistoryAdded} item(ns) de histórico sincronizado(s)"
            + (result.Failures > 0 ? $"; {result.Failures} indisponível(is)." : ".");
    }

    private void Exportar()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvar log consolidado",
            Filter = "Arquivo de texto|*.txt",
            FileName = $"Comunicador-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _repository.ExportarTxt(dialog.FileName, ItensFiltrados.Cast<LogEntry>());
            Status = $"Log salvo em {dialog.FileName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Não foi possível salvar o log: {ex.Message}";
            Logger.Error("Falha ao exportar o log consolidado.", "logs", ex.ToString());
        }
    }

    private bool Filtrar(object obj)
    {
        if (obj is not LogEntry item) return false;
        if (Nivel == "Erros" && item.Nivel != NivelLog.Erro) return false;
        if (Nivel == "Avisos" && item.Nivel != NivelLog.Aviso) return false;
        if (Nivel == "Informações" && item.Nivel != NivelLog.Info) return false;
        if (Origem == "Painéis" && item.TipoOrigem != "panel") return false;
        if (Origem == "Receptores" && item.TipoOrigem != "receiver") return false;
        if (string.IsNullOrWhiteSpace(Filtro)) return true;
        return item.OrigemNome.Contains(Filtro, StringComparison.OrdinalIgnoreCase)
            || item.Categoria.Contains(Filtro, StringComparison.OrdinalIgnoreCase)
            || item.Mensagem.Contains(Filtro, StringComparison.OrdinalIgnoreCase)
            || (item.Detalhes?.Contains(Filtro, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
