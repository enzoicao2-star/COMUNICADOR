using System.Collections.ObjectModel;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Services;

namespace Comunicador.ViewModels;

public sealed class MensagensViewModel : ViewModelBase
{
    private readonly ComputadoresViewModel _computadores;
    private readonly EnviadorNotificacoes _enviador;
    private readonly HistoricoRepository _historico;

    private string _titulo = string.Empty;
    private string _mensagem = string.Empty;
    private bool _permitirResposta = true;
    private string? _statusOperacao;

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

    public ICommand EnviarCommand { get; }
    public ICommand AtualizarDestinatariosCommand { get; }

    public MensagensViewModel(
        ComputadoresViewModel computadores, EnviadorNotificacoes enviador, HistoricoRepository historico)
    {
        _computadores = computadores;
        _enviador = enviador;
        _historico = historico;

        EnviarCommand = new AsyncRelayCommand(EnviarAsync, PodeEnviar);
        AtualizarDestinatariosCommand = new RelayCommand(_ => AtualizarDestinatarios());

        _computadores.Computadores.CollectionChanged += (_, _) => AtualizarDestinatarios();
        AtualizarDestinatarios();
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

    private bool PodeEnviar() =>
        !string.IsNullOrWhiteSpace(Titulo)
        && !string.IsNullOrWhiteSpace(Mensagem)
        && Destinatarios.Any(d => d.Selecionado);

    private async Task EnviarAsync()
    {
        var selecionados = Destinatarios.Where(d => d.Selecionado).ToList();
        StatusOperacao = $"Enviando para {selecionados.Count} computador(es)...";

        foreach (var destino in selecionados)
        {
            var computador = destino.Computador;
            var entry = new HistoricoEntry
            {
                ComputadorId = computador.Id,
                ComputadorNome = computador.Nome,
                Titulo = Titulo,
                Mensagem = Mensagem,
                Status = StatusEnvio.Enviando,
            };
            _historico.Adicionar(entry);

            var resultado = await _enviador
                .EnviarAsync(computador, Titulo, Mensagem, PermitirResposta)
                .ConfigureAwait(true);

            _historico.AtualizarExistente(entry.Id, item => AplicarResultado(item, resultado, PermitirResposta));
        }

        StatusOperacao = "Envio concluído.";
        Titulo = string.Empty;
        Mensagem = string.Empty;
    }

    private static void AplicarResultado(HistoricoEntry item, NotificationResult resultado, bool permitirResposta)
    {
        if (!resultado.Delivered)
        {
            item.Status = StatusEnvio.Erro;
            item.ErroDetalhe = resultado.ErrorMessage;
        }
        else if (resultado.GotReply)
        {
            item.Status = StatusEnvio.Respondido;
            item.RespostaTexto = resultado.ReplyText;
        }
        else if (permitirResposta)
        {
            item.Status = StatusEnvio.SemResposta;
        }
        else if (resultado.WasShown)
        {
            item.Status = StatusEnvio.Exibido;
        }
        else
        {
            item.Status = StatusEnvio.Entregue;
        }
    }
}
