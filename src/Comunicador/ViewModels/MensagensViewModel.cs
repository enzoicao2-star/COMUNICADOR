using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.Services;
using Microsoft.Win32;

namespace Comunicador.ViewModels;

public sealed class MensagensViewModel : ViewModelBase
{
    private readonly ComputadoresViewModel _computadores;
    private readonly EnviadorNotificacoes _enviador;
    private readonly HistoricoRepository _historico;

    private string _titulo = string.Empty;
    private string _mensagem = string.Empty;
    private bool _permitirResposta = true;
    private bool _exibirImagemCentral;
    private bool _exibirAvisoObrigatorio;
    private double _tempoImagemSegundos = 15;
    private bool _permitirFecharImagem = true;
    private bool _tocarSom = true;
    private string _tipoSom = ProtocolConstants.SoundType.Information;
    private string _posicaoAviso = ProtocolConstants.ToastPosition.BottomRight;
    private string _corDestaque = "#0067C0";
    private double _escalaTexto = 100;
    private double _tempoAvisoSegundos = 20;
    private string? _caminhoImagem;
    private string? _nomeImagem;
    private string? _mimeImagem;
    private byte[]? _dadosImagem;
    private string? _statusOperacao;

    public ObservableCollection<ComputadorSelecionavel> Destinatarios { get; } = new();
    public ObservableCollection<DestinoMonitor> MonitoresDestino { get; } = new();

    /// <summary>Botões de resposta rápida que vão junto com o aviso.</summary>
    public ObservableCollection<BotaoRespostaEditavel> Botoes { get; } = new();

    private string _novoBotaoRotulo = string.Empty;
    private string _novoBotaoUrl = string.Empty;

    public string NovoBotaoRotulo
    {
        get => _novoBotaoRotulo;
        set => SetField(ref _novoBotaoRotulo, value);
    }

    public string NovoBotaoUrl
    {
        get => _novoBotaoUrl;
        set => SetField(ref _novoBotaoUrl, value);
    }

    public ICommand AdicionarBotaoCommand { get; }
    public ICommand RemoverBotaoCommand { get; }

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

    public bool ExibirImagemCentral
    {
        get => _exibirImagemCentral;
        set
        {
            if (SetField(ref _exibirImagemCentral, value))
            {
                if (value && _exibirAvisoObrigatorio)
                {
                    _exibirAvisoObrigatorio = false;
                    OnPropertyChanged(nameof(ExibirAvisoObrigatorio));
                }
                OnPropertyChanged(nameof(DescricaoFormato));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? CaminhoImagem
    {
        get => _caminhoImagem;
        private set => SetField(ref _caminhoImagem, value);
    }

    public string? NomeImagem
    {
        get => _nomeImagem;
        private set => SetField(ref _nomeImagem, value);
    }

    public bool TemImagem => _dadosImagem is { Length: > 0 };

    public string DescricaoFormato => ExibirImagemCentral
        ? "Aparecerá somente a imagem, centralizada e sem moldura. Título e mensagem não são necessários."
        : ExibirAvisoObrigatorio
            ? "O computador ficará coberto pelo aviso até a pessoa clicar em OK."
        : "O aviso aparecerá no canto inferior direito, no estilo do Windows.";

    public bool ExibirAvisoObrigatorio
    {
        get => _exibirAvisoObrigatorio;
        set
        {
            if (SetField(ref _exibirAvisoObrigatorio, value))
            {
                if (value && _exibirImagemCentral)
                {
                    _exibirImagemCentral = false;
                    OnPropertyChanged(nameof(ExibirImagemCentral));
                }
                OnPropertyChanged(nameof(DescricaoFormato));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double TempoImagemSegundos
    {
        get => _tempoImagemSegundos;
        set => SetField(ref _tempoImagemSegundos, Math.Round(Math.Clamp(
            value, ProtocolConstants.MinImageDurationSeconds, 300)));
    }

    public bool PermitirFecharImagem
    {
        get => _permitirFecharImagem;
        set => SetField(ref _permitirFecharImagem, value);
    }

    public bool TocarSom { get => _tocarSom; set => SetField(ref _tocarSom, value); }
    public string TipoSom { get => _tipoSom; set => SetField(ref _tipoSom, value); }
    public string PosicaoAviso { get => _posicaoAviso; set => SetField(ref _posicaoAviso, value); }

    public string CorDestaque
    {
        get => _corDestaque;
        set => SetField(ref _corDestaque, value);
    }

    public double EscalaTexto
    {
        get => _escalaTexto;
        set => SetField(ref _escalaTexto, Math.Round(Math.Clamp(
            value, ProtocolConstants.MinFontScalePercent, ProtocolConstants.MaxFontScalePercent)));
    }

    public double TempoAvisoSegundos
    {
        get => _tempoAvisoSegundos;
        set => SetField(ref _tempoAvisoSegundos, Math.Round(Math.Clamp(
            value, ProtocolConstants.MinToastDurationSeconds, ProtocolConstants.MaxToastDurationSeconds)));
    }

    public string? StatusOperacao
    {
        get => _statusOperacao;
        set => SetField(ref _statusOperacao, value);
    }

    public ICommand EnviarCommand { get; }
    public ICommand AtualizarDestinatariosCommand { get; }
    public ICommand SelecionarImagemCommand { get; }
    public ICommand RemoverImagemCommand { get; }
    public ICommand AdicionarImagemMonitorCommand { get; }
    public ICommand RemoverImagemMonitorCommand { get; }

    public MensagensViewModel(
        ComputadoresViewModel computadores, EnviadorNotificacoes enviador, HistoricoRepository historico)
    {
        _computadores = computadores;
        _enviador = enviador;
        _historico = historico;

        EnviarCommand = new AsyncRelayCommand(EnviarAsync, PodeEnviar);
        AtualizarDestinatariosCommand = new RelayCommand(_ => AtualizarDestinatarios());
        SelecionarImagemCommand = new RelayCommand(_ => SelecionarImagem());
        RemoverImagemCommand = new RelayCommand(_ => RemoverImagem(), _ => TemImagem);
        AdicionarImagemMonitorCommand = new RelayCommand(param =>
        {
            if (param is DestinoMonitor destino)
            {
                AdicionarImagensAoMonitor(destino);
            }
        });
        RemoverImagemMonitorCommand = new RelayCommand(param =>
        {
            if (param is ImagemMonitorEditavel imagem)
            {
                foreach (var destino in MonitoresDestino)
                {
                    if (destino.Imagens.Remove(imagem))
                    {
                        break;
                    }
                }
                CommandManager.InvalidateRequerySuggested();
            }
        });

        AdicionarBotaoCommand = new RelayCommand(_ => AdicionarBotao(), _ => PodeAdicionarBotao());
        RemoverBotaoCommand = new RelayCommand(param =>
        {
            if (param is BotaoRespostaEditavel botao)
            {
                Botoes.Remove(botao);
            }
        });

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

        AtualizarMonitoresDestino();
    }

    private void AtualizarMonitoresDestino()
    {
        var imagensExistentes = MonitoresDestino.ToDictionary(d => d.Chave, d => d.Imagens.ToList());
        MonitoresDestino.Clear();

        foreach (var computador in _computadores.Computadores.Where(c => c.Pareado))
        {
            var monitores = computador.Monitores.Count > 0
                ? computador.Monitores
                : new List<MonitorInfo> { new() { Index = 0, Name = "Monitor principal", Primary = true } };

            foreach (var monitor in monitores.OrderBy(m => m.Index))
            {
                var destino = new DestinoMonitor { Computador = computador, Monitor = monitor };
                if (imagensExistentes.TryGetValue(destino.Chave, out var imagens))
                {
                    foreach (var imagem in imagens)
                    {
                        destino.Imagens.Add(imagem);
                    }
                }
                MonitoresDestino.Add(destino);
            }
        }
    }

    private bool PodeAdicionarBotao()
    {
        if (string.IsNullOrWhiteSpace(NovoBotaoRotulo) || Botoes.Count >= ProtocolConstants.MaxBotoes)
        {
            return false;
        }

        // URL é opcional, mas se preenchida precisa ser http/https
        return string.IsNullOrWhiteSpace(NovoBotaoUrl) || BotaoResposta.UrlPermitida(NovoBotaoUrl.Trim());
    }

    private void AdicionarBotao()
    {
        Botoes.Add(new BotaoRespostaEditavel
        {
            Rotulo = NovoBotaoRotulo.Trim(),
            Url = string.IsNullOrWhiteSpace(NovoBotaoUrl) ? null : NovoBotaoUrl.Trim(),
        });

        NovoBotaoRotulo = string.Empty;
        NovoBotaoUrl = string.Empty;
    }

    private bool PodeEnviar()
    {
        var temConteudo = ExibirImagemCentral
            ? TemImagem || DestinatariosComImagemEspecifica()
            : !string.IsNullOrWhiteSpace(Titulo) && !string.IsNullOrWhiteSpace(Mensagem);
        return temConteudo && Destinatarios.Any(d => d.Selecionado);
    }

    private bool DestinatariosComImagemEspecifica()
    {
        var selecionados = Destinatarios.Where(d => d.Selecionado).Select(d => d.Computador.Id).ToList();
        return selecionados.Count > 0
            && selecionados.All(id => MonitoresDestino.Any(m => m.Computador.Id == id && m.Imagens.Count > 0));
    }

    private void AdicionarImagensAoMonitor(DestinoMonitor destino)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Escolher imagens para {destino.Titulo}",
            Filter = "Imagens permitidas|*.png;*.jpg;*.jpeg;*.gif;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|GIF|*.gif|Bitmap|*.bmp",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var caminho in dialog.FileNames)
        {
            try
            {
                var arquivo = new FileInfo(caminho);
                if (arquivo.Length <= 0 || arquivo.Length > ProtocolConstants.MaxImageBytes)
                {
                    StatusOperacao = $"{arquivo.Name}: cada imagem pode ter no máximo 4 MB.";
                    continue;
                }

                var mime = MimePelaExtensao(caminho);
                var dados = File.ReadAllBytes(caminho);
                if (!ConteudoImagem.MimePermitido(mime) || !ConteudoImagem.AssinaturaCorresponde(mime, dados))
                {
                    StatusOperacao = $"{arquivo.Name}: formato inválido.";
                    continue;
                }

                var totalDoComputador = MonitoresDestino
                    .Where(m => m.Computador.Id == destino.Computador.Id)
                    .SelectMany(m => m.Imagens)
                    .Sum(i => (long)i.Dados.Length);
                var quantidadeDoComputador = MonitoresDestino
                    .Where(m => m.Computador.Id == destino.Computador.Id)
                    .Sum(m => m.Imagens.Count);
                if (quantidadeDoComputador >= ProtocolConstants.MaxScreenImages
                    || totalDoComputador + dados.Length > ProtocolConstants.MaxTotalImageBytes)
                {
                    StatusOperacao = "Limite por computador: 12 imagens e 16 MB no total.";
                    break;
                }

                destino.Imagens.Add(new ImagemMonitorEditavel
                {
                    Caminho = caminho,
                    Nome = arquivo.Name,
                    MimeType = mime,
                    Dados = dados,
                });
                StatusOperacao = $"{arquivo.Name} adicionada ao {destino.Titulo}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusOperacao = $"Não foi possível abrir a imagem: {ex.Message}";
            }
        }

        ExibirImagemCentral = true;
        CommandManager.InvalidateRequerySuggested();
    }

    private static string MimePelaExtensao(string caminho) => Path.GetExtension(caminho).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => string.Empty,
    };

    private List<ImagemMonitor> CriarImagensPorMonitor(string computadorId) => MonitoresDestino
        .Where(m => m.Computador.Id == computadorId)
        .SelectMany(m => m.Imagens.Select(i => i.ParaProtocolo(m.Monitor.Index)))
        .ToList();

    private void SelecionarImagem()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher imagem para o aviso",
            Filter = "Imagens permitidas|*.png;*.jpg;*.jpeg;*.gif;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|GIF|*.gif|Bitmap|*.bmp",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var arquivo = new FileInfo(dialog.FileName);
            if (arquivo.Length <= 0 || arquivo.Length > ProtocolConstants.MaxImageBytes)
            {
                StatusOperacao = "A imagem precisa ter no máximo 4 MB.";
                return;
            }

            var mime = MimePelaExtensao(dialog.FileName);
            var dados = File.ReadAllBytes(dialog.FileName);
            if (!ConteudoImagem.MimePermitido(mime) || !ConteudoImagem.AssinaturaCorresponde(mime, dados))
            {
                StatusOperacao = "Arquivo inválido. Escolha uma imagem PNG, JPEG, GIF ou BMP.";
                return;
            }

            _dadosImagem = dados;
            _mimeImagem = mime;
            CaminhoImagem = dialog.FileName;
            NomeImagem = arquivo.Name;
            ExibirImagemCentral = true;
            StatusOperacao = $"Imagem selecionada: {arquivo.Name} ({arquivo.Length / 1024d:0.#} KB).";
            OnPropertyChanged(nameof(TemImagem));
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusOperacao = $"Não foi possível abrir a imagem: {ex.Message}";
        }
    }

    private void RemoverImagem()
    {
        _dadosImagem = null;
        _mimeImagem = null;
        CaminhoImagem = null;
        NomeImagem = null;
        ExibirImagemCentral = false;
        OnPropertyChanged(nameof(TemImagem));
        StatusOperacao = "Imagem removida.";
        CommandManager.InvalidateRequerySuggested();
    }

    private ConteudoImagem? CriarConteudoImagem()
    {
        if (!ExibirImagemCentral || _dadosImagem is not { Length: > 0 } || _mimeImagem is null || NomeImagem is null)
        {
            return null;
        }

        return new ConteudoImagem
        {
            Name = NomeImagem,
            MimeType = _mimeImagem,
            DataBase64 = Convert.ToBase64String(_dadosImagem),
        };
    }

    private async Task EnviarAsync()
    {
        var selecionados = Destinatarios.Where(d => d.Selecionado).ToList();
        var modoExibicao = ExibirImagemCentral
            ? ProtocolConstants.DisplayMode.CenterImage
            : ExibirAvisoObrigatorio
                ? ProtocolConstants.DisplayMode.CenterAlert
                : ProtocolConstants.DisplayMode.Toast;
        // Imagem central é um conteúdo visual puro. O fechamento manual ocorre
        // clicando na própria imagem, sem botões, campos ou textos sobrepostos.
        var permiteInteracao = !ExibirAvisoObrigatorio && !ExibirImagemCentral;
        var botoesProtocolo = permiteInteracao
            ? Botoes.Select(b => b.ParaProtocolo()).ToList()
            : new List<BotaoResposta>();
        var imagem = CriarConteudoImagem();
        var duracaoImagem = ExibirImagemCentral ? (int)TempoImagemSegundos : (int?)null;
        var permitirFechar = ExibirImagemCentral ? PermitirFecharImagem : (bool?)null;
        var permitirRespostaEfetiva = PermitirResposta && permiteInteracao;
        var aparencia = new AparenciaNotificacao
        {
            AccentColor = CorDestaque.Trim(),
            FontScalePercent = (int)EscalaTexto,
            PlaySound = TocarSom,
            SoundType = TipoSom,
            ToastDurationSeconds = (int)TempoAvisoSegundos,
            ToastPosition = PosicaoAviso,
        };
        StatusOperacao = $"Enviando para {selecionados.Count} computador(es)...";

        var enviados = 0;
        var erros = new List<string>();

        foreach (var destino in selecionados)
        {
            var computador = destino.Computador;
            var imagensPorMonitor = CriarImagensPorMonitor(computador.Id);
            var imagemParaEsteComputador = imagensPorMonitor.Count > 0 ? null : imagem;
            var tituloHistorico = ExibirImagemCentral && string.IsNullOrWhiteSpace(Titulo)
                ? "Imagem"
                : Titulo;
            var mensagemHistorico = ExibirImagemCentral && string.IsNullOrWhiteSpace(Mensagem)
                ? NomeImagem ?? "Imagem enviada"
                : Mensagem;
            var entry = new HistoricoEntry
            {
                ComputadorId = computador.Id,
                ComputadorNome = computador.Nome,
                Titulo = tituloHistorico,
                Mensagem = mensagemHistorico,
                Status = StatusEnvio.Enviando,
            };
            _historico.Adicionar(entry);

            var resultado = await _enviador
                .EnviarAsync(
                    computador, Titulo, Mensagem, permitirRespostaEfetiva, botoesProtocolo,
                    modoExibicao: modoExibicao,
                    imagem: imagemParaEsteComputador,
                    imagensPorMonitor: imagensPorMonitor,
                    duracaoImagemSegundos: duracaoImagem,
                    permitirFecharManualmente: permitirFechar,
                    aparencia: aparencia)
                .ConfigureAwait(true);

            _historico.AtualizarExistente(
                entry.Id, item => AplicarResultado(item, resultado, permitirRespostaEfetiva));
            if (resultado.Delivered)
            {
                enviados++;
            }
            else
            {
                var detalhe = resultado.ErrorMessage ?? "falha sem detalhe";
                erros.Add($"{computador.Nome}: {detalhe}");
                Logger.Error(
                    $"Falha ao enviar mensagem para {computador.NomeExibicao} ({computador.EnderecoIp}:{computador.PortaTcp}).",
                    "envio",
                    $"Título: {Titulo} | Modo: {modoExibicao} | Erro: {detalhe}");
            }
        }

        var nomeConteudo = ExibirImagemCentral ? "Imagem" : "Mensagem";
        StatusOperacao = erros.Count == 0
            ? $"{nomeConteudo} exibida em {enviados} computador(es)."
            : $"Exibida em {enviados}; falhou em {erros.Count}. {string.Join(" | ", erros)}";
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
