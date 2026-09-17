using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Comunicador.Protocol;
using Comunicador.Services;

namespace Comunicador.Views;

/// <summary>Reproduz vídeo sem cartão, moldura ou texto e áudio sem qualquer
/// elemento visível. Os arquivos temporários existem apenas durante a reprodução.</summary>
internal sealed class MidiaRecebidaWindow : Window
{
    private readonly MediaElement _player;
    private readonly string _arquivoTemporario;
    private readonly bool _repetir;
    private readonly bool _somenteAudio;
    private readonly Rect _areaMonitor;
    private readonly int _larguraPercentual;
    private bool _fechando;

    private MidiaRecebidaWindow(
        string arquivoTemporario, bool repetir, bool somenteAudio,
        Rect areaMonitor, int larguraPercentual, bool permitirFechar)
    {
        _arquivoTemporario = arquivoTemporario;
        _repetir = repetir;
        _somenteAudio = somenteAudio;
        _areaMonitor = areaMonitor;
        _larguraPercentual = larguraPercentual;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        _player = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Uniform,
            Source = new Uri(arquivoTemporario, UriKind.Absolute),
            Volume = 1,
        };
        Content = _player;

        if (somenteAudio)
        {
            Width = 1;
            Height = 1;
            Left = -32000;
            Top = -32000;
            Opacity = 0;
            IsHitTestVisible = false;
        }
        else
        {
            Width = Math.Max(160, areaMonitor.Width * larguraPercentual / 100d);
            Height = Math.Min(areaMonitor.Height, Math.Max(90, Width * 9d / 16d));
            Left = areaMonitor.Left + (areaMonitor.Width - Width) / 2d;
            Top = areaMonitor.Top + (areaMonitor.Height - Height) / 2d;
            if (permitirFechar)
            {
                Cursor = Cursors.Hand;
                MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    SolicitarFechamento?.Invoke();
                };
                KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        SolicitarFechamento?.Invoke();
                    }
                };
            }
        }

        _player.MediaOpened += (_, _) => AoAbrirMidia();
        _player.MediaEnded += (_, _) =>
        {
            if (_repetir)
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
            }
            else
            {
                Close();
            }
        };
        _player.MediaFailed += (_, e) =>
        {
            Logger.Error($"Falha ao reproduzir mídia recebida: {e.ErrorException?.Message ?? "erro desconhecido"}");
            Close();
        };
        Loaded += (_, _) => _player.Play();
        Closed += (_, _) => Limpar();
    }

    public Action? SolicitarFechamento { get; set; }

    private void AoAbrirMidia()
    {
        if (_somenteAudio || _player.NaturalVideoWidth <= 0 || _player.NaturalVideoHeight <= 0)
        {
            return;
        }

        var larguraMaxima = Math.Max(1, _areaMonitor.Width * _larguraPercentual / 100d);
        var alturaMaxima = Math.Max(1, _areaMonitor.Height - 20);
        var escala = Math.Min(
            larguraMaxima / _player.NaturalVideoWidth,
            alturaMaxima / _player.NaturalVideoHeight);
        Width = Math.Max(1, _player.NaturalVideoWidth * escala);
        Height = Math.Max(1, _player.NaturalVideoHeight * escala);
        Left = _areaMonitor.Left + (_areaMonitor.Width - Width) / 2d;
        Top = _areaMonitor.Top + (_areaMonitor.Height - Height) / 2d;
    }

    private void Limpar()
    {
        if (_fechando)
        {
            return;
        }
        _fechando = true;
        try
        {
            _player.Stop();
            _player.Close();
        }
        catch
        {
            // A mídia pode já ter sido fechada pelo pipeline do Windows.
        }
        try
        {
            File.Delete(_arquivoTemporario);
        }
        catch (IOException)
        {
            // O Windows ainda pode estar liberando o arquivo; o temporário será
            // descartado automaticamente pelo sistema depois.
        }
    }

    public static Task<string?> MostrarAsync(
        ConteudoVideo? video,
        IReadOnlyList<VideoMonitor>? videosPorMonitor,
        bool repetirVideo,
        ConteudoAudio? audio,
        bool repetirAudio,
        int? duracaoSegundos,
        bool permitirFechar)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            tcs.TrySetResult(null);
            return tcs.Task;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            var janelas = new List<MidiaRecebidaWindow>();
            DispatcherTimer? timer = null;
            var concluido = false;

            void Concluir()
            {
                if (concluido)
                {
                    return;
                }
                concluido = true;
                timer?.Stop();
                foreach (var janela in janelas.Where(w => w.IsVisible).ToList())
                {
                    janela.Close();
                }
                tcs.TrySetResult(null);
            }

            try
            {
                if (audio is not null)
                {
                    var caminho = GravarTemporario(
                        audio.DataBase64, ConteudoAudio.ExtensaoTemporaria(audio.MimeType));
                    var janela = new MidiaRecebidaWindow(
                        caminho, repetirAudio, true, SystemParameters.WorkArea, 100, false);
                    janela.SolicitarFechamento = Concluir;
                    janelas.Add(janela);
                }
                else
                {
                    var itens = videosPorMonitor is { Count: > 0 }
                        ? videosPorMonitor
                        : video is not null
                            ? new List<VideoMonitor>
                            {
                                new() { MonitorIndex = 0, WidthPercent = 70, Video = video },
                            }
                            : [];
                    foreach (var item in itens)
                    {
                        var caminho = GravarTemporario(
                            item.Video.DataBase64,
                            ConteudoVideo.ExtensaoTemporaria(item.Video.MimeType));
                        var janela = new MidiaRecebidaWindow(
                            caminho, repetirVideo, false,
                            ObterAreaMonitor(item.MonitorIndex), item.WidthPercent, permitirFechar);
                        janela.SolicitarFechamento = Concluir;
                        janelas.Add(janela);
                    }
                }

                if (janelas.Count == 0)
                {
                    Concluir();
                    return;
                }

                var restantes = janelas.Count;
                foreach (var janela in janelas)
                {
                    janela.Closed += (_, _) =>
                    {
                        restantes--;
                        if (restantes <= 0)
                        {
                            Concluir();
                        }
                    };
                    janela.Show();
                }

                if (duracaoSegundos.HasValue)
                {
                    timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(Math.Clamp(
                            duracaoSegundos.Value,
                            ProtocolConstants.MinImageDurationSeconds,
                            ProtocolConstants.MaxImageDurationSeconds)),
                    };
                    timer.Tick += (_, _) => Concluir();
                    timer.Start();
                }
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                Logger.Error($"Mídia recebida não pôde ser preparada: {ex.Message}");
                Concluir();
            }
        }));
        return tcs.Task;
    }

    private static string GravarTemporario(string dadosBase64, string extensao)
    {
        var pasta = Path.Combine(Path.GetTempPath(), "Comunicador", "midia");
        Directory.CreateDirectory(pasta);
        var caminho = Path.Combine(pasta, $"{Guid.NewGuid():N}{extensao}");
        File.WriteAllBytes(caminho, Convert.FromBase64String(dadosBase64));
        return caminho;
    }

    private static Rect ObterAreaMonitor(int monitorIndex)
    {
        try
        {
            var telas = MonitorInfo.ListarLocais();
            if (telas.Count == 0)
            {
                return SystemParameters.WorkArea;
            }
            var indice = Math.Clamp(monitorIndex, 0, telas.Count - 1);
            var area = telas[indice];
            return new Rect(area.X, area.Y, area.Width, area.Height);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }
}
