using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Comunicador.Protocol;
using Comunicador.Services;

namespace Comunicador.Views;

public partial class NotificacaoRecebidaWindow : Window
{
    private static readonly TimeSpan FechamentoAutomatico = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer? _autoCloseTimer;

    public string? Resultado { get; private set; }

    public NotificacaoRecebidaWindow(
        string sender, string title, string message, bool allowReply, IReadOnlyList<BotaoResposta>? botoes = null)
    {
        InitializeComponent();

        DeSenderText.Text = sender;
        TituloText.Text = title;
        MensagemText.Text = message;

        if (botoes is { Count: > 0 })
        {
            BotoesPanel.ItemsSource = botoes;
            BotoesPanel.Visibility = Visibility.Visible;
        }

        if (allowReply)
        {
            RespostaPanel.Visibility = Visibility.Visible;
            Loaded += (_, _) => RespostaBox.Focus();
        }
        else
        {
            OkPanel.Visibility = Visibility.Visible;
            _autoCloseTimer = new DispatcherTimer { Interval = FechamentoAutomatico };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };
            _autoCloseTimer.Start();
        }

        // Posiciona e anima so depois do layout: com SizeToContent a altura
        // final so existe apos o Loaded.
        Loaded += (_, _) =>
        {
            PosicionarCantoInferiorDireito(this);
            AnimarEntrada(this);
        };
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
        _autoCloseTimer?.Stop();
        Close();
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
        string sender, string title, string message, bool allowReply, IReadOnlyList<BotaoResposta>? botoes = null)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            var window = new NotificacaoRecebidaWindow(sender, title, message, allowReply, botoes)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            window.Closed += (_, _) => tcs.TrySetResult(window.Resultado);
            window.Show();
        }));

        return tcs.Task;
    }

    private static void PosicionarCantoInferiorDireito(Window window)
    {
        var area = SystemParameters.WorkArea;
        window.Left = area.Right - window.ActualWidth;
        window.Top = area.Bottom - window.ActualHeight;
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
