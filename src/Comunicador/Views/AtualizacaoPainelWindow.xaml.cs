using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Comunicador.Services;

namespace Comunicador.Views;

public partial class AtualizacaoPainelWindow : UserControl
{
    private readonly string _progressPath;
    private readonly DispatcherTimer _timer;
    private bool _restartRaised;

    public event EventHandler? ReinicioPronto;
    public event EventHandler? FinalizadaSemReinicio;
    public event EventHandler? FalhaOcorreu;

    public AtualizacaoPainelWindow(string progressPath)
    {
        InitializeComponent();
        _progressPath = progressPath;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += AtualizarProgresso;
        _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private async void AtualizarProgresso(object? sender, EventArgs e)
    {
        if (!File.Exists(_progressPath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_progressPath);
            var progresso = JsonSerializer.Deserialize<PanelUpdateProgress>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (progresso is null) return;

            Progresso.Value = Math.Clamp(progresso.Percent, 0, 100);
            Porcentagem.Text = $"{Progresso.Value:0}%";
            StatusTexto.Text = progresso.Message;
            if (progresso.Phase == "restart_wait")
            {
                AvisoReinicio.Visibility = Visibility.Visible;
                AvisoReinicio.Text = progresso.RemainingSeconds > 0
                    ? $"A atualização terminou. O Comunicador será reiniciado automaticamente em {progresso.RemainingSeconds} segundos."
                    : "Reiniciando o Comunicador...";
            }
            else if (progresso.Phase is "launching" or "done")
            {
                if (_restartRaised) return;
                _restartRaised = true;
                ReinicioPronto?.Invoke(this, EventArgs.Empty);
            }
            else if (progresso.Phase == "up_to_date")
            {
                _timer.Stop();
                FinalizadaSemReinicio?.Invoke(this, EventArgs.Empty);
            }
            else if (progresso.Phase == "failed")
            {
                _timer.Stop();
                Spinner.Visibility = Visibility.Collapsed;
                AvisoReinicio.Visibility = Visibility.Collapsed;
                Fechar.Visibility = Visibility.Visible;
                StatusTexto.TextWrapping = TextWrapping.Wrap;
                Progresso.Foreground = System.Windows.Media.Brushes.IndianRed;
                FalhaOcorreu?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // O atualizador substitui o arquivo de progresso atomicamente; uma
            // leitura durante essa troca é transitória e será repetida no próximo tick.
        }
    }

    public event EventHandler? VoltarSolicitado;

    private void Fechar_Click(object sender, RoutedEventArgs e) => VoltarSolicitado?.Invoke(this, EventArgs.Empty);
}
