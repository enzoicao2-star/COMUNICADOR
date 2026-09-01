using System.Collections.ObjectModel;
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

    private string _titulo = string.Empty;
    private string _mensagem = string.Empty;
    private DateTime _dataHora = DateTime.Now.AddMinutes(5);
    private DateTime? _dataSelecionada = DateTime.Today;
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

    public DateTime? DataSelecionada
    {
        get => _dataSelecionada;
        set
        {
            if (SetField(ref _dataSelecionada, value))
            {
                AtualizarDataHora();
            }
        }
    }

    public string HoraTexto
    {
        get => _horaTexto;
        set
        {
            if (SetField(ref _horaTexto, value))
            {
                AtualizarDataHora();
            }
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
        LembreteSchedulerService scheduler)
    {
        _store = store;
        _computadores = computadores;
        _historico = historico;

        foreach (var lembrete in _store.Load())
        {
            Lembretes.Add(lembrete);
        }

        scheduler.NotificacaoEnviada += OnNotificacaoEnviada;
        scheduler.LembreteConcluido += _ => UiDispatcher.Invoke(Persist);

        CriarCommand = new RelayCommand(_ => Criar(), _ => PodeCriar());
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
        }
        else if (resultado.GotReply)
        {
            entry.Status = StatusEnvio.Respondido;
            entry.RespostaTexto = resultado.ReplyText;
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
        && DataHora > DateTime.Now;

    private void Criar()
    {
        var lembrete = new Lembrete
        {
            Titulo = Titulo,
            Mensagem = Mensagem,
            DataHora = DataHora,
            PermitirResposta = PermitirResposta,
            ComputadorIds = Destinatarios.Where(d => d.Selecionado).Select(d => d.Computador.Id).ToList(),
        };

        Lembretes.Add(lembrete);
        Persist();

        Titulo = string.Empty;
        Mensagem = string.Empty;
        var proximo = DateTime.Now.AddMinutes(5);
        _dataSelecionada = proximo.Date;
        OnPropertyChanged(nameof(DataSelecionada));
        HoraTexto = proximo.ToString("HH:mm");
        StatusOperacao = "Lembrete criado.";
    }

    private void AtualizarDataHora()
    {
        if (DataSelecionada.HasValue && TimeSpan.TryParse(HoraTexto, out var hora))
        {
            DataHora = DataSelecionada.Value.Date + hora;
        }
    }

    private void Persist() => _store.Save(Lembretes);
}
