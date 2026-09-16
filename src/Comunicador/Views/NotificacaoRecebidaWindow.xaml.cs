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
    private readonly int? _monitorIndex;
    private readonly string _posicaoToast;
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
        int? monitorIndex = null)
    {
        InitializeComponent();

        aparencia ??= new AparenciaNotificacao();
        _monitorIndex = monitorIndex;
        _posicaoToast = aparencia.ToastPosition;

        DeSenderText.Text = "Comunicador";
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
            IconeComunicador.Background = brush;
            ResponderButton.Background = brush;
        }
        catch (FormatException)
        {
            // A validação do protocolo já impede esta situação; conserva a cor padrão.
        }

        if (modoExibicao == ProtocolConstants.DisplayMode.CenterAlert)
        {
            _avisoCentral = true;
            _avisoObrigatorio = true;
            DeSenderText.Text = "Comunicador";
            SizeToContent = SizeToContent.Manual;
            PlanoFundo.Background = new SolidColorBrush(Color.FromArgb(224, 14, 14, 14));
            Cartao.Width = 580;
            Cartao.MaxWidth = 580;
            Cartao.HorizontalAlignment = HorizontalAlignment.Center;
            Cartao.VerticalAlignment = VerticalAlignment.Center;
            Cartao.BorderBrush = IconeComunicador.Background;
            Cartao.BorderThickness = new Thickness(2);
            TituloText.FontSize = Math.Max(TituloText.FontSize, 22 * escala);
            MensagemText.FontSize = Math.Max(MensagemText.FontSize, 15 * escala);
            OkButton.Background = IconeComunicador.Background;
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
                ImagensAvisoPanel.ItemsSource = itens.Select(item => new ImagemRenderizada
                {
                    Source = CarregarImagem(item.Image.DataBase64),
                    Width = Math.Max(120, (areaMonitor.Width - 70) * item.WidthPercent / 100d),
                    MaxHeight = Math.Max(160, areaMonitor.Height * 0.58),
                }).ToList();
                ImagemPanel.Visibility = Visibility.Visible;
                _avisoCentral = true;

                Width = Math.Max(420, areaMonitor.Width - 34);
                MaxHeight = Math.Max(320, areaMonitor.Height - 34);
                TituloText.FontSize = 18;
                MensagemText.FontSize = 14;
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
            {
                Logger.Error($"Imagem recebida não pôde ser exibida: {ex.Message}");
            }
        }

        var interacaoPermitida = !_avisoObrigatorio
            && (!_avisoCentral || permitirFecharManualmente != false);
        if (!interacaoPermitida)
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
        else if (interacaoPermitida || _avisoObrigatorio)
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
        if (_avisoObrigatorio && !_fechamentoConfirmado
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

        if (botao.TemLink)
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
        AparenciaNotificacao? aparencia = null)
    {
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
                    aparencia, grupo.MonitorIndex)
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
        public BitmapImage Source { get; init; } = null!;
        public double Width { get; init; }
        public double MaxHeight { get; init; }
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
