using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Comunicador.Models;
using Comunicador.ViewModels;

namespace Comunicador.Views;

public partial class MidiasAtivasView : UserControl
{
    private ImagemPersistenteAtiva? _arrastando;
    private double _xAnterior;
    private double _yAnterior;
    private readonly DispatcherTimer _tempoTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _consultouAoAbrir;

    public MidiasAtivasView()
    {
        InitializeComponent();
        _tempoTimer.Tick += (_, _) => (DataContext as MidiasAtivasViewModel)?.AtualizarTemposRestantes();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                if (DataContext is MidiasAtivasViewModel vm)
                {
                    vm.AtualizarTemposRestantes();
                    if (!_consultouAoAbrir)
                    {
                        _consultouAoAbrir = true;
                        await vm.AtualizarAsync();
                    }
                }
                _tempoTimer.Start();
            }
            else _tempoTimer.Stop();
        };
        Unloaded += (_, _) => _tempoTimer.Stop();
    }

    private async void ControlarMidia_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MidiasAtivasViewModel vm
            || sender is not Button { DataContext: ImagemPersistenteAtiva imagem, Tag: string acao }) return;
        await vm.ControlarAsync(imagem, acao);
    }

    private void MidiaPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: ImagemPersistenteAtiva imagem, Parent: Canvas canvas }) return;
        _arrastando = imagem;
        _xAnterior = imagem.XPercent;
        _yAnterior = imagem.YPercent;
        canvas.CaptureMouse();
        e.Handled = true;
    }

    private void MidiaPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_arrastando is null || sender is not Canvas canvas || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(canvas);
        _arrastando.XPercent = point.X / 320 * 100;
        _arrastando.YPercent = point.Y / 180 * 100;
    }

    private async void MidiaPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_arrastando is null || sender is not Canvas canvas) return;
        canvas.ReleaseMouseCapture();
        var imagem = _arrastando;
        _arrastando = null;
        if (DataContext is MidiasAtivasViewModel vm)
            await vm.ControlarAsync(imagem, "move", _xAnterior, _yAnterior);
    }
}
