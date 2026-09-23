using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using Comunicador.Protocol;
using Comunicador.Services;

namespace Comunicador.Views;

public partial class NotificacaoRecebidaWindow : Window
{
    private static readonly TimeSpan FechamentoAutomatico = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer? _autoCloseTimer;
    private readonly bool _avisoCentral;
    private readonly bool _avisoObrigatorio;
    private readonly bool _imagemCentral;
    private readonly bool _bloquearFechamentoManual;
    private readonly int? _monitorIndex;
    private readonly string _posicaoToast;
    private readonly bool _previewMode;
    private bool _fechamentoConfirmado;

    public string? Resultado { get; private set; }

    public NotificacaoRecebidaWindow(
        string sender, string title, string message, bool allowReply,
        IReadOnlyList<BotaoResposta>? botoes = null,
        string? modoExibicao = null,
        ConteudoImagem? imagem = null,
        IReadOnlyList<ImagemMonitor>? imagensPorMonitor = null,
        int? duracaoImagemSegundos = null,
        bool? permitirFecharManualmente = null,
        AparenciaNotificacao? aparencia = null,
        int? monitorIndex = null,
        bool previewMode = false)
    {
        InitializeComponent();

        aparencia ??= new AparenciaNotificacao();
        _previewMode = previewMode;
        _monitorIndex = monitorIndex;
        _posicaoToast = aparencia.ToastPosition;

        DeSenderText.Text = sender;
        TituloText.Text = title;
        MensagemText.Text = message;

        var escala = aparencia.FontScalePercent / 100d;
        TituloText.FontSize *= escala;
        MensagemText.FontSize *= escala;
        try
        {
            var cor = (Color)ColorConverter.ConvertFromString(aparencia.AccentColor);
            var brush = new SolidColorBrush(cor);
            brush.Freeze();
            ResponderButton.Background = brush;
        }
        catch (FormatException)
        {
            // A validação do protocolo já impede esta situação; conserva a cor padrão.
        }

        if (modoExibicao is ProtocolConstants.DisplayMode.CenterAlert or ProtocolConstants.DisplayMode.CenterMessage)
        {
            _avisoCentral = true;
            _avisoObrigatorio = modoExibicao == ProtocolConstants.DisplayMode.CenterAlert && !previewMode;
            DeSenderText.Text = sender;
            SizeToContent = SizeToContent.Manual;
            PlanoFundo.Background = new SolidColorBrush(Color.FromArgb(224, 14, 14, 14));
            Cartao.Width = 580;
            Cartao.MaxWidth = 580;
            Cartao.HorizontalAlignment = HorizontalAlignment.Center;
            Cartao.VerticalAlignment = VerticalAlignment.Center;
            Cartao.BorderBrush = ResponderButton.Background;
            Cartao.BorderThickness = new Thickness(2);
            TituloText.FontSize = Math.Max(TituloText.FontSize, 22 * escala);
            MensagemText.FontSize = Math.Max(MensagemText.FontSize, 15 * escala);
            OkButton.Background = ResponderButton.Background;
        }

        if (modoExibicao == ProtocolConstants.DisplayMode.CenterImage
            && (imagem is not null || imagensPorMonitor is { Count: > 0 }))
        {
            try
            {
                var itens = imagensPorMonitor is { Count: > 0 }
                    ? imagensPorMonitor
                    : new List<ImagemMonitor>
                    {
                        new() { MonitorIndex = monitorIndex ?? 0, WidthPercent = 70, Image = imagem! },
                    };
                var areaMonitor = ObterAreaMonitor(monitorIndex);
                var colunas = itens.Count == 1 ? 1 : 2;
                var linhas = (int)Math.Ceiling(itens.Count / (double)colunas);
                var larguraCelula = Math.Max(1, (areaMonitor.Width - 40) / colunas);
                var alturaCelula = Math.Max(1, (areaMonitor.Height - 40) / linhas);
                var renderizadas = itens.Select(item => CriarImagemRenderizada(
                    item, areaMonitor, larguraCelula, alturaCelula)).ToList();

                ImagensAvisoPanel.ItemsSource = renderizadas;
                ImagensAvisoPanel.Width = itens.Count == 1
                    ? renderizadas[0].Width
                    : Math.Min(areaMonitor.Width - 40, larguraCelula * colunas);
                ImagemPanel.Visibility = Visibility.Visible;
                _avisoCentral = true;
                _imagemCentral = true;
                _bloquearFechamentoManual = permitirFecharManualmente == false && !previewMode;

                // Modo imagem puro: nenhum cartão, cabeçalho, texto, botão,
                // borda ou fundo fica visível ao redor do arquivo.
                CabecalhoPanel.Visibility = Visibility.Collapsed;
                TituloText.Visibility = Visibility.Collapsed;
                MensagemText.Visibility = Visibility.Collapsed;
                BotoesPanel.Visibility = Visibility.Collapsed;
                RespostaPanel.Visibility = Visibility.Collapsed;
                OkPanel.Visibility = Visibility.Collapsed;
                FecharX.Visibility = Visibility.Collapsed;
                Cartao.Margin = new Thickness(0);
                Cartao.Padding = new Thickness(0);
                Cartao.Background = Brushes.Transparent;
                Cartao.BorderThickness = new Thickness(0);
                Cartao.Effect = null;
                ConteudoCartao.Margin = new Thickness(0);
                ImagemPanel.Margin = new Thickness(0);
                ImagemPanel.Padding = new Thickness(0);
                ImagemPanel.Background = Brushes.Transparent;
                ImagemPanel.BorderThickness = new Thickness(0);
                Width = double.NaN;
                Height = double.NaN;
                MaxWidth = Math.Max(1, areaMonitor.Width - 20);
                MaxHeight = Math.Max(1, areaMonitor.Height - 20);
                SizeToContent = SizeToContent.WidthAndHeight;

                if (permitirFecharManualmente != false)
                {
                    ImagemPanel.Cursor = Cursors.Hand;
                    ImagemPanel.MouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        _fechamentoConfirmado = true;
                        _autoCloseTimer?.Stop();
                        Close();
                    };
                }
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
            {
                Logger.Error($"Imagem recebida não pôde ser exibida: {ex.Message}");
            }
        }

        var interacaoPermitida = !_avisoObrigatorio && !_imagemCentral;
        if (!interacaoPermitida && !previewMode)
        {
            FecharX.Visibility = Visibility.Collapsed;
        }

        if (interacaoPermitida && botoes is { Count: > 0 })
        {
            BotoesPanel.ItemsSource = botoes;
            BotoesPanel.Visibility = Visibility.Visible;
        }

        if (interacaoPermitida && allowReply)
        {
            RespostaPanel.Visibility = Visibility.Visible;
            Loaded += (_, _) => RespostaBox.Focus();
        }
        else if ((interacaoPermitida || _avisoObrigatorio) && botoes is not { Count: > 0 })
        {
            OkPanel.Visibility = Visibility.Visible;
        }

        TimeSpan? tempoAteFechar = _avisoObrigatorio
            ? null
            : _avisoCentral
            ? TimeSpan.FromSeconds(Math.Clamp(
                duracaoImagemSegundos ?? 15,
                ProtocolConstants.MinImageDurationSeconds,
                ProtocolConstants.MaxImageDurationSeconds))
            : allowReply ? null : TimeSpan.FromSeconds(Math.Clamp(
                aparencia.ToastDurationSeconds,
                ProtocolConstants.MinToastDurationSeconds,
                ProtocolConstants.MaxToastDurationSeconds));

        if (tempoAteFechar.HasValue)
        {
            _autoCloseTimer = new DispatcherTimer { Interval = tempoAteFechar.Value };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                _fechamentoConfirmado = true;
                Close();
            };
            _autoCloseTimer.Start();
        }

        Closed += (_, _) => _autoCloseTimer?.Stop();

        // Posiciona e anima so depois do layout: com SizeToContent a altura
        // final so existe apos o Loaded.
        Loaded += (_, _) =>
        {
            if (_avisoObrigatorio)
            {
                PosicionarBloqueioMonitor(this, _monitorIndex);
                AnimarEntradaCentral(this);
            }
            else if (_avisoCentral)
            {
                PosicionarCentro(this, _monitorIndex);
                AnimarEntradaCentral(this);
            }
            else
            {
                PosicionarCantoInferiorDireito(this);
                AnimarEntrada(this);
            }
        };

        Closing += AoTentarFechar;
        if (aparencia.PlaySound)
        {
            TocarSom(aparencia.SoundType);
        }
    }

    private void Responder_Click(object sender, RoutedEventArgs e)
    {
        Resultado = string.IsNullOrWhiteSpace(RespostaBox.Text) ? null : RespostaBox.Text.Trim();
        Close();
    }

    private void RespostaBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Responder_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void Fechar_Click(object sender, RoutedEventArgs e)
    {
        _fechamentoConfirmado = true;
        _autoCloseTimer?.Stop();
        Close();
    }

    private void PlanoFundo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_avisoObrigatorio && !Cartao.IsMouseOver)
        {
            SystemSounds.Exclamation.Play();
            e.Handled = true;
        }
    }

    private void AoTentarFechar(object? sender, CancelEventArgs e)
    {
        if ((_avisoObrigatorio || _bloquearFechamentoManual) && !_fechamentoConfirmado
            && Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            e.Cancel = true;
            SystemSounds.Exclamation.Play();
        }
    }

    /// <summary>Clique num botão de resposta rápida: devolve o rótulo como resposta e,
    /// se o botão tiver link, abre no navegador padrão. A URL é revalidada aqui —
    /// ela veio pela rede, então nunca confiamos só na validação de quem enviou.</summary>
    private void BotaoResposta_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BotaoResposta botao })
        {
            return;
        }

        if (botao.TemLink && !_previewMode)
        {
            if (BotaoResposta.UrlPermitida(botao.Url))
            {
                try
                {
                    // UseShellExecute manda para o navegador padrão do usuário
                    Process.Start(new ProcessStartInfo(botao.Url!) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Logger.Error($"Falha ao abrir o link do botão '{botao.Label}': {ex.Message}");
                }
            }
            else
            {
                Logger.Error($"Link recusado no botão '{botao.Label}': só http/https são permitidos.");
            }
        }

        Resultado = botao.Label;
        _autoCloseTimer?.Stop();
        Close();
    }

    public static Task<string?> MostrarAsync(
        string sender, string title, string message, bool allowReply,
        IReadOnlyList<BotaoResposta>? botoes = null,
        string? modoExibicao = null,
        ConteudoImagem? imagem = null,
        IReadOnlyList<ImagemMonitor>? imagensPorMonitor = null,
        int? duracaoImagemSegundos = null,
        bool? permitirFecharManualmente = null,
        AparenciaNotificacao? aparencia = null,
        ConteudoVideo? video = null,
        IReadOnlyList<VideoMonitor>? videosPorMonitor = null,
        bool? repetirVideo = null,
        ConteudoAudio? audio = null,
        bool? repetirAudio = null,
        bool previewMode = false)
    {
        if (modoExibicao is ProtocolConstants.DisplayMode.CenterVideo or ProtocolConstants.DisplayMode.Audio)
        {
            return MidiaRecebidaWindow.MostrarAsync(
                video, videosPorMonitor, repetirVideo == true,
                audio, repetirAudio == true, duracaoImagemSegundos,
                previewMode || permitirFecharManualmente != false);
        }

        var tipoSom = aparencia?.SoundType;
        var usarJanelaWindows = !allowReply && botoes is not { Count: > 0 }
            && imagem is null && imagensPorMonitor is not { Count: > 0 }
            && modoExibicao is ProtocolConstants.DisplayMode.Toast or ProtocolConstants.DisplayMode.CenterMessage
            && tipoSom is ProtocolConstants.SoundType.Warning or ProtocolConstants.SoundType.Error;
        if (usarJanelaWindows)
        {
            var nativeResult = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                var icon = tipoSom == ProtocolConstants.SoundType.Error
                    ? MessageBoxImage.Error : MessageBoxImage.Warning;
                var body = string.IsNullOrWhiteSpace(title) ? message : $"{title}\n\n{message}";
                MessageBox.Show(body, sender, MessageBoxButton.OK, icon);
                nativeResult.TrySetResult(null);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return nativeResult.Task;
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            var grupos = modoExibicao == ProtocolConstants.DisplayMode.CenterImage
                    && imagensPorMonitor is { Count: > 0 }
                ? imagensPorMonitor.GroupBy(i => i.MonitorIndex)
                    .Select(g => (MonitorIndex: (int?)g.Key, Imagens: (IReadOnlyList<ImagemMonitor>)g.ToList()))
                    .ToList()
                : new List<(int? MonitorIndex, IReadOnlyList<ImagemMonitor> Imagens)>
                {
                    (null, Array.Empty<ImagemMonitor>()),
                };

            var windows = new List<NotificacaoRecebidaWindow>();
            var concluido = false;
            foreach (var grupo in grupos)
            {
                var window = new NotificacaoRecebidaWindow(
                    sender, title, message, allowReply, botoes, modoExibicao, imagem,
                    grupo.Imagens, duracaoImagemSegundos, permitirFecharManualmente,
                    aparencia, grupo.MonitorIndex, previewMode)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                };
                windows.Add(window);
                window.Closed += (_, _) =>
                {
                    if (concluido)
                    {
                        return;
                    }
                    concluido = true;
                    foreach (var outra in windows.Where(w => w != window && w.IsVisible).ToList())
                    {
                        outra._fechamentoConfirmado = true;
                        outra.Close();
                    }
                    tcs.TrySetResult(window.Resultado);
                };
                window.Show();
            }
        }));

        return tcs.Task;
    }

    private void PosicionarCantoInferiorDireito(Window window)
    {
        var area = SystemParameters.WorkArea;
        window.Left = area.Right - window.ActualWidth;
        window.Top = _posicaoToast == ProtocolConstants.ToastPosition.TopRight
            ? area.Top
            : area.Bottom - window.ActualHeight;
    }

    private static void PosicionarCentro(Window window, int? monitorIndex)
    {
        var area = ObterAreaMonitor(monitorIndex);
        window.Left = area.Left + (area.Width - window.ActualWidth) / 2;
        window.Top = area.Top + (area.Height - window.ActualHeight) / 2;
    }

    private static void PosicionarBloqueioMonitor(Window window, int? monitorIndex)
    {
        var area = ObterAreaMonitor(monitorIndex);
        window.Left = area.Left;
        window.Top = area.Top;
        window.Width = area.Width;
        window.Height = area.Height;
    }

    private static Rect ObterAreaMonitor(int? monitorIndex)
    {
        try
        {
            var telas = MonitorInfo.ListarLocais();
            if (telas.Count == 0)
            {
                return SystemParameters.WorkArea;
            }
            var indice = Math.Clamp(monitorIndex ?? 0, 0, telas.Count - 1);
            var area = telas[indice];
            return new Rect(area.X, area.Y, area.Width, area.Height);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private static BitmapImage CarregarImagem(string dadosBase64)
    {
        var dados = Convert.FromBase64String(dadosBase64);
        using var stream = new MemoryStream(dados, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImagemRenderizada CriarImagemRenderizada(
        ImagemMonitor item, Rect areaMonitor, double larguraCelula, double alturaCelula)
    {
        var bitmap = CarregarImagem(item.Image.DataBase64);
        var larguraDesejada = Math.Max(1, (areaMonitor.Width - 40) * item.WidthPercent / 100d);
        var larguraMaxima = Math.Min(larguraDesejada, larguraCelula);
        var alturaMaxima = Math.Min(areaMonitor.Height - 40, alturaCelula);
        var escala = Math.Min(
            larguraMaxima / Math.Max(1, bitmap.PixelWidth),
            alturaMaxima / Math.Max(1, bitmap.PixelHeight));

        return new ImagemRenderizada
        {
            DataBase64 = item.Image.DataBase64,
            MimeType = item.Image.MimeType,
            Width = Math.Max(1, bitmap.PixelWidth * escala),
            Height = Math.Max(1, bitmap.PixelHeight * escala),
        };
    }

    private static void TocarSom(string tipo)
    {
        if (tipo == ProtocolConstants.SoundType.Error)
        {
            SystemSounds.Hand.Play();
        }
        else if (tipo == ProtocolConstants.SoundType.Warning)
        {
            SystemSounds.Exclamation.Play();
        }
        else
        {
            SystemSounds.Asterisk.Play();
        }
    }

    private sealed class ImagemRenderizada
    {
        public string DataBase64 { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public double Width { get; init; }
        public double Height { get; init; }
    }

    private static void AnimarEntradaCentral(Window window)
    {
        window.Opacity = 0;
        window.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }

    /// <summary>Deslize curto de baixo para cima, como as notificacoes do Windows.</summary>
    private static void AnimarEntrada(Window window)
    {
        var destino = window.Top;
        window.Top = destino + 28;
        window.Opacity = 0;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        window.BeginAnimation(TopProperty, new DoubleAnimation(destino, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = easing,
        });
        window.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }
}
