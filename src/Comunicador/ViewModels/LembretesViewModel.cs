using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Services;
using Comunicador.Storage;

namespace Comunicador.ViewModels;

public sealed class LembretesViewModel : ViewModelBase
{
    private readonly JsonStore<Lembrete> _store;
    private readonly ComputadoresViewModel _computadores;
    private readonly HistoricoRepository _historico;
    private readonly CloudSyncService _cloud;

    private string _titulo = string.Empty;
    private string _mensagem = string.Empty;
    private DateTime _dataHora = DateTime.Now.AddMinutes(5);
    private string _dataTexto = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
    private string _horaTexto = DateTime.Now.AddMinutes(5).ToString("HH:mm");
    private bool _permitirResposta = true;
    private string? _statusOperacao;

    public ObservableCollection<Lembrete> Lembretes { get; } = new();
    public ObservableCollection<ComputadorSelecionavel> Destinatarios { get; } = new();

    public string Titulo
    {
        get => _titulo;
        set => SetField(ref _titulo, value);
    }

    public string Mensagem
    {
        get => _mensagem;
        set => SetField(ref _mensagem, value);
    }

    public DateTime DataHora
    {
        get => _dataHora;
        set => SetField(ref _dataHora, value);
    }

    public string DataTexto
    {
        get => _dataTexto;
        set
        {
            if (!SetField(ref _dataTexto, value)) return;
            AtualizarDataHora();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string HoraTexto
    {
        get => _horaTexto;
        set
        {
            if (!SetField(ref _horaTexto, value, nameof(HoraTexto))) return;
            if (TentarObterData(DataTexto, out var data) && TentarObterHora(value, out var hora))
            {
                DataHora = data.Date + hora;
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool PermitirResposta
    {
        get => _permitirResposta;
        set => SetField(ref _permitirResposta, value);
    }

    public string? StatusOperacao
    {
        get => _statusOperacao;
        set => SetField(ref _statusOperacao, value);
    }

    public ICommand CriarCommand { get; }
    public ICommand RemoverCommand { get; }
    public ICommand AtualizarDestinatariosCommand { get; }

    public LembretesViewModel(
        JsonStore<Lembrete> store, ComputadoresViewModel computadores, HistoricoRepository historico,
        LembreteSchedulerService scheduler, CloudSyncService cloud)
    {
        _store = store;
        _computadores = computadores;
        _historico = historico;
        _cloud = cloud;

        var proximoHorario = ArredondarParaProximoIntervalo(DateTime.Now.AddMinutes(5));
        _dataTexto = proximoHorario.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        _horaTexto = proximoHorario.ToString("HH:mm");
        _dataHora = proximoHorario;

        foreach (var lembrete in _store.Load())
        {
            Lembretes.Add(lembrete);
        }

        scheduler.NotificacaoEnviada += OnNotificacaoEnviada;
        scheduler.LembreteConcluido += _ => UiDispatcher.Invoke(Persist);

        CriarCommand = new AsyncRelayCommand(_ => CriarAsync(), _ => PodeCriar());
        RemoverCommand = new RelayCommand(param =>
        {
            if (param is Lembrete lembrete)
            {
                Lembretes.Remove(lembrete);
                Persist();
            }
        });
        AtualizarDestinatariosCommand = new RelayCommand(_ => AtualizarDestinatarios());
        _computadores.Computadores.CollectionChanged += (_, _) => AtualizarDestinatarios();
        AtualizarDestinatarios();
    }

    public IReadOnlyList<Lembrete> Snapshot() => Lembretes.ToList();

    private void OnNotificacaoEnviada(Lembrete lembrete, Computador computador, NotificationResult resultado)
    {
        var entry = new HistoricoEntry
        {
            ComputadorId = computador.Id,
            ComputadorNome = computador.Nome,
            Titulo = lembrete.Titulo,
            Mensagem = lembrete.Mensagem,
        };

        if (!resultado.Delivered)
        {
            entry.Status = StatusEnvio.Erro;
            entry.ErroDetalhe = resultado.ErrorMessage;
            Logger.Error(
                $"Falha ao enviar o lembrete '{lembrete.Titulo}' para {computador.NomeExibicao}.",
                "lembrete", resultado.ErrorMessage);
        }
        else if (resultado.GotReply)
        {
            entry.Status = StatusEnvio.Respondido;
            entry.RespostaTexto = resultado.ReplyText;
            UiDispatcher.Invoke(() => _ = Views.NotificacaoRecebidaWindow.MostrarAsync(
                computador.NomeExibicao, "Resposta ao lembrete",
                resultado.ReplyText ?? "O usuário confirmou o recebimento.", allowReply: false));
        }
        else if (lembrete.PermitirResposta)
        {
            entry.Status = StatusEnvio.SemResposta;
        }
        else
        {
            entry.Status = StatusEnvio.Exibido;
        }

        _historico.Adicionar(entry);
    }

    private void AtualizarDestinatarios()
    {
        var idsSelecionados = Destinatarios.Where(d => d.Selecionado).Select(d => d.Computador.Id).ToHashSet();
        Destinatarios.Clear();
        foreach (var computador in _computadores.Computadores.Where(c => c.Pareado))
        {
            Destinatarios.Add(new ComputadorSelecionavel(computador) { Selecionado = idsSelecionados.Contains(computador.Id) });
        }
    }

    private bool PodeCriar() =>
        !string.IsNullOrWhiteSpace(Titulo)
        && !string.IsNullOrWhiteSpace(Mensagem)
        && Destinatarios.Any(d => d.Selecionado)
        && TentarObterDataHora(out _);

    private async Task CriarAsync()
    {
        if (!TentarObterDataHora(out var dataHora)) return;

        var lembrete = new Lembrete
        {
            Titulo = Titulo,
            Mensagem = Mensagem,
            DataHora = dataHora,
            PermitirResposta = PermitirResposta,
            ComputadorIds = Destinatarios.Where(d => d.Selecionado).Select(d => d.Computador.Id).ToList(),
        };

        Lembretes.Add(lembrete);
        Persist();

        try
        {
            await Task.WhenAll(lembrete.ComputadorIds.Select(id => _cloud.QueueReminderAsync(
                id, lembrete.DataHora, lembrete.Titulo, lembrete.Mensagem, lembrete.PermitirResposta)))
                .ConfigureAwait(true);
            lembrete.AgendadoNaNuvem = true;
            Persist();
            StatusOperacao = "Lembrete agendado na nuvem; será entregue mesmo com o painel fechado.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            StatusOperacao = $"Lembrete salvo neste painel; envio local será usado: {ex.Message}";
        }

        Titulo = string.Empty;
        Mensagem = string.Empty;
        var proximo = ArredondarParaProximoIntervalo(DateTime.Now.AddMinutes(5));
        _dataTexto = proximo.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        OnPropertyChanged(nameof(DataTexto));
        _horaTexto = proximo.ToString("HH:mm");
        OnPropertyChanged(nameof(HoraTexto));
        AtualizarDataHora();
    }

    private void AtualizarDataHora()
    {
        if (TentarObterData(DataTexto, out var data) && TentarObterHora(_horaTexto, out var hora))
        {
            DataHora = data.Date + hora;
        }
    }

    private bool TentarObterDataHora(out DateTime dataHora)
    {
        dataHora = default;
        if (!TentarObterData(DataTexto, out var data) || !TentarObterHora(HoraTexto, out var hora)) return false;
        dataHora = data.Date + hora;
        return dataHora > DateTime.Now;
    }

    private static bool TentarObterData(string? texto, out DateTime data) =>
        DateTime.TryParseExact(texto, ["dd/MM/yyyy", "d/M/yyyy"],
            CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out data);

    private static bool TentarObterHora(string? texto, out TimeSpan hora)
    {
        hora = default;
        return TimeSpan.TryParse(texto, CultureInfo.CurrentCulture, out hora)
            && hora >= TimeSpan.Zero
            && hora < TimeSpan.FromDays(1)
            && hora.Seconds == 0;
    }

    private static DateTime ArredondarParaProximoIntervalo(DateTime valor) =>
        new DateTime(valor.Year, valor.Month, valor.Day, valor.Hour, 0, 0).AddMinutes(((valor.Minute / 30) + 1) * 30);

    private void Persist() => _store.Save(Lembretes);
}
