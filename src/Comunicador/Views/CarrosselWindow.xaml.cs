using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.ViewModels;
using Microsoft.Win32;

namespace Comunicador.Views;

public partial class CarrosselWindow : Window
{
    private readonly EnviadorNotificacoes _enviador;
    private readonly Func<bool> _canStart;
    private readonly Func<bool> _canStop;
    private readonly ObservableCollection<string> _images = [];
    private readonly IReadOnlyList<ComputadorSelecionavel> _recipients;
    private readonly CancellationTokenSource _closing = new();
    private bool _busy;

    public CarrosselWindow(EnviadorNotificacoes enviador, IEnumerable<ComputadorSelecionavel> recipients,
        Func<bool> canStart, Func<bool> canStop)
    {
        InitializeComponent();
        _enviador = enviador;
        _canStart = canStart;
        _canStop = canStop;
        _recipients = recipients.ToList();
        ImagesList.ItemsSource = _images;
        RecipientsList.ItemsSource = _recipients;
        Closed += (_, _) => _closing.Cancel();
    }

    private void AddImages_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha imagens PNG ou JPEG para o carrossel",
            Filter = "Imagens PNG/JPEG|*.png;*.jpg;*.jpeg",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        var rejected = 0;
        foreach (var path in dialog.FileNames)
        {
            try
            {
                if (new FileInfo(path).Length > ProtocolConstants.MaxImageBytes)
                {
                    rejected++;
                    continue;
                }
                _images.Add(path);
            }
            catch (IOException) { rejected++; }
            catch (UnauthorizedAccessException) { rejected++; }
        }
        StatusText.Text = rejected == 0
            ? $"{_images.Count} imagem(ns) na sequência."
            : $"{_images.Count} imagem(ns) na sequência; {rejected} arquivo(s) acima de 4 MB ou inacessíveis.";
    }

    private void RemoveImages_Click(object sender, RoutedEventArgs e)
    {
        foreach (var path in ImagesList.SelectedItems.Cast<string>().ToList()) _images.Remove(path);
        StatusText.Text = $"{_images.Count} imagem(ns) na sequência.";
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        if (ImagesList.SelectedItem is not string selected) return;
        var index = _images.IndexOf(selected);
        var next = index + delta;
        if (next < 0 || next >= _images.Count) return;
        _images.Move(index, next);
        ImagesList.SelectedItem = selected;
    }

    private bool TryIntervals(out int min, out int max)
    {
        var random = RandomRadio.IsChecked == true;
        var minText = random ? RandomMin.Text : FixedMinutes.Text;
        var maxText = random ? RandomMax.Text : FixedMinutes.Text;
        var minValid = int.TryParse(minText, NumberStyles.None, CultureInfo.InvariantCulture, out min);
        var maxValid = int.TryParse(maxText, NumberStyles.None, CultureInfo.InvariantCulture, out max);
        var valid = minValid && maxValid
            && min >= 1 && max >= min && max <= 10080;
        if (!valid) StatusText.Text = "Informe minutos inteiros de 1 a 10080, com o máximo maior ou igual ao mínimo.";
        return valid;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_canStart()) { StatusText.Text = "O acesso administrativo às imagens do sistema foi desativado."; return; }
        var targets = _recipients.Where(r => r.Selecionado).Select(r => r.Computador).ToList();
        if (targets.Count == 0 || _images.Count == 0)
        {
            StatusText.Text = "Selecione pelo menos um computador e uma imagem.";
            return;
        }
        if (!TryIntervals(out var min, out var max)) return;
        var target = ((ComboBoxItem)TargetCombo.SelectedItem).Tag.ToString()!;
        var paths = _images.ToArray();
        var errors = new List<string>();
        SetBusy(true);
        TransferProgress.Maximum = targets.Count * (paths.Length + 2);
        TransferProgress.Value = 0;
        try
        {
            foreach (var computer in targets)
            {
                var session = Guid.NewGuid().ToString();
                var begin = new CarouselCommand
                {
                    Action = "begin", SessionId = session, Target = target,
                    Count = paths.Length, MinMinutes = min, MaxMinutes = max,
                    Repeat = RepeatCheck.IsChecked == true,
                };
                StatusText.Text = $"{computer.NomeExibicao}: preparando carrossel…";
                var result = await SendAsync(computer, begin, null);
                if (!result.Delivered)
                {
                    errors.Add($"{computer.NomeExibicao}: {result.ErrorMessage}");
                    TransferProgress.Value += paths.Length + 2;
                    continue;
                }
                TransferProgress.Value++;

                var completed = true;
                for (var index = 0; index < paths.Length; index++)
                {
                    _closing.Token.ThrowIfCancellationRequested();
                    var bytes = await File.ReadAllBytesAsync(paths[index], _closing.Token);
                    if (bytes.Length > ProtocolConstants.MaxImageBytes)
                        throw new InvalidDataException($"{Path.GetFileName(paths[index])} excede 4 MB.");
                    var mime = Path.GetExtension(paths[index]).Equals(".png", StringComparison.OrdinalIgnoreCase)
                        ? "image/png" : "image/jpeg";
                    var image = new ConteudoImagem
                    {
                        Name = Path.GetFileName(paths[index]), MimeType = mime,
                        DataBase64 = Convert.ToBase64String(bytes),
                    };
                    StatusText.Text = $"{computer.NomeExibicao}: imagem {index + 1}/{paths.Length}…";
                    result = await SendAsync(computer, new CarouselCommand
                    {
                        Action = "item", SessionId = session, Index = index,
                    }, image);
                    TransferProgress.Value++;
                    if (result.Delivered) continue;
                    errors.Add($"{computer.NomeExibicao}, imagem {index + 1}: {result.ErrorMessage}");
                    TransferProgress.Value += paths.Length - index;
                    completed = false;
                    break;
                }
                if (!completed) continue;
                StatusText.Text = $"{computer.NomeExibicao}: ativando…";
                result = await SendAsync(computer, new CarouselCommand
                {
                    Action = "commit", SessionId = session,
                }, null);
                TransferProgress.Value++;
                if (!result.Delivered) errors.Add($"{computer.NomeExibicao}: {result.ErrorMessage}");
            }
            StatusText.Text = errors.Count == 0
                ? $"Carrossel ativado em {targets.Count} computador(es)."
                : $"Concluído com {errors.Count} falha(s): {string.Join("; ", errors)}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = $"Falha ao carregar imagem: {ex.Message}";
        }
        finally { if (IsLoaded) SetBusy(false); }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_canStop()) { StatusText.Text = "O acesso administrativo foi desativado."; return; }
        var targets = _recipients.Where(r => r.Selecionado).Select(r => r.Computador).ToList();
        if (targets.Count == 0)
        {
            StatusText.Text = "Selecione os computadores onde deseja desligar o carrossel.";
            return;
        }
        SetBusy(true);
        TransferProgress.Maximum = targets.Count;
        TransferProgress.Value = 0;
        var errors = new List<string>();
        try
        {
            foreach (var computer in targets)
            {
                StatusText.Text = $"Desligando em {computer.NomeExibicao}…";
                var result = await SendAsync(computer, new CarouselCommand
                {
                    Action = "stop", SessionId = Guid.NewGuid().ToString(),
                }, null);
                TransferProgress.Value++;
                if (!result.Delivered) errors.Add($"{computer.NomeExibicao}: {result.ErrorMessage}");
            }
            StatusText.Text = errors.Count == 0
                ? $"Carrossel desligado em {targets.Count} computador(es)."
                : $"Falha ao desligar: {string.Join("; ", errors)}";
        }
        catch (OperationCanceledException) { }
        finally { if (IsLoaded) SetBusy(false); }
    }

    private Task<NotificationResult> SendAsync(Models.Computador computer, CarouselCommand carousel,
        ConteudoImagem? image) => _enviador.EnviarAsync(computer, "", "", false,
            modoExibicao: ProtocolConstants.DisplayMode.Carousel, imagem: image,
            ct: _closing.Token, carousel: carousel);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        StartButton.IsEnabled = !busy;
        StopButton.IsEnabled = !busy;
    }
}
