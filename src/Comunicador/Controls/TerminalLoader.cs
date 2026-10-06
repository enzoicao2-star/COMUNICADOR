using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Comunicador.Controls;

/// <summary>Animação discreta para comandos remotos pendentes, na cor do tema atual.</summary>
public sealed class TerminalLoader : FrameworkElement
{
    private const int Columns = 40;
    private const int Rows = 5;
    private const int BlockWidth = 3;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private int _position;
    private int _direction = 1;

    public TerminalLoader()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) =>
        {
            _position += _direction;
            if (_position >= Columns - BlockWidth - 1) { _position = Columns - BlockWidth - 1; _direction = -1; }
            else if (_position <= 0) { _position = 0; _direction = 1; }
            InvalidateVisual();
        };
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (IsLoaded && IsVisible) _timer.Start();
        else _timer.Stop();
    }

    protected override Size MeasureOverride(Size availableSize) => new(400, 72);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var accent = Application.Current?.TryFindResource("AccentBrush") as SolidColorBrush
            ?? Brushes.CornflowerBlue;
        var rowHeight = 12d;
        var typeface = new Typeface("Consolas");
        var letters = Enumerable.Repeat('.', Columns).ToArray();
        for (var trail = 0; trail < 3; trail++)
        {
            var index = _direction > 0 ? _position - 1 - trail : _position + BlockWidth + trail;
            if (index >= 0 && index < Columns) letters[index] = "▓▒░"[trail];
        }
        var text = new string(letters);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 13, accent,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var cell = formatted.WidthIncludingTrailingWhitespace / Columns;
        for (var row = 0; row < Rows; row++)
            dc.DrawText(formatted, new Point(0, row * rowHeight));
        dc.DrawRectangle(accent, null,
            new Rect(_position * cell, 0, BlockWidth * cell, Rows * rowHeight + 2));
    }
}
