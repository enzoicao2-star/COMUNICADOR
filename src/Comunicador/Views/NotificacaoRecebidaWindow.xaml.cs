using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Comunicador.Views;

public partial class NotificacaoRecebidaWindow : Window
{
    private static readonly TimeSpan FechamentoAutomatico = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer? _autoCloseTimer;

    public string? Resultado { get; private set; }

    public NotificacaoRecebidaWindow(string sender, string title, string message, bool allowReply)
    {
        InitializeComponent();

        DeSenderText.Text = $"De: {sender}";
        TituloText.Text = title;
        MensagemText.Text = message;

        if (allowReply)
        {
            RespostaPanel.Visibility = Visibility.Visible;
            Loaded += (_, _) => RespostaBox.Focus();
        }
        else
        {
            OkPanel.Visibility = Visibility.Visible;
            _autoCloseTimer = new DispatcherTimer { Interval = FechamentoAutomatico };
            _autoCloseTimer.Tick += (_, _) => Close();
            _autoCloseTimer.Start();
        }
    }

    private void Responder_Click(object sender, RoutedEventArgs e)
    {
        Resultado = RespostaBox.Text;
        Close();
    }

    private void RespostaBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Resultado = RespostaBox.Text;
            Close();
        }
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();

    public static Task<string?> MostrarAsync(string sender, string title, string message, bool allowReply)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            var window = new NotificacaoRecebidaWindow(sender, title, message, allowReply)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            PosicionarCantoInferiorDireito(window);
            window.Closed += (_, _) => tcs.TrySetResult(window.Resultado);
            window.Show();
        }));

        return tcs.Task;
    }

    private static void PosicionarCantoInferiorDireito(Window window)
    {
        var area = SystemParameters.WorkArea;
        window.Left = area.Right - window.Width - 20;
        window.Top = area.Bottom - window.Height - 20;
    }
}
