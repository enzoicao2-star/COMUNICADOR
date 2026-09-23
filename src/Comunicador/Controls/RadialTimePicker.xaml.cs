using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Comunicador.Controls;

public partial class RadialTimePicker : UserControl
{
    private static readonly (string Label, int Minutes, int StartAngle)[] Adjustments =
    [
        ("+15m", 15, 0), ("+30m", 30, 45), ("+1h", 60, 90), ("+2h", 120, 135),
        ("−2h", -120, 180), ("−1h", -60, 225), ("−30m", -30, 270), ("−15m", -15, 315),
    ];

    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(
        nameof(Time), typeof(string), typeof(RadialTimePicker),
        new FrameworkPropertyMetadata("12:00", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Time { get => (string)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    private bool MenuFixado { get; set; }

    public RadialTimePicker()
    {
        InitializeComponent();
        CriarFatias();
    }

    private void CriarFatias()
    {
        const double center = 160;
        for (var i = 0; i < Adjustments.Length; i++)
        {
            var adjustment = Adjustments[i];
            var geometry = CriarFatia(center, center, 78, 143, adjustment.StartAngle, adjustment.StartAngle + 45);
            var path = new Path
            {
                Data = geometry,
                StrokeThickness = 3,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = adjustment.Minutes,
                ToolTip = adjustment.Minutes > 0
                    ? $"Adiantar {adjustment.Label.TrimStart('+')}"
                    : $"Atrasar {adjustment.Label.TrimStart('−', '-')}"
            };
            path.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
            path.SetResourceReference(Shape.StrokeProperty, "BackgroundBrush");
            path.MouseEnter += Fatia_MouseEnter;
            path.MouseLeave += Fatia_MouseLeave;
            path.MouseLeftButtonUp += Fatia_MouseLeftButtonUp;
            Fatias.Children.Add(path);

            var labelPosition = Polar(center, center, 110, adjustment.StartAngle + 22.5);
            var label = new TextBlock
            {
                Text = adjustment.Label,
                Width = 58,
                Height = 24,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty,
                adjustment.Minutes > 0 ? "AccentBrush" : "TextSecondaryBrush");
            Canvas.SetLeft(label, labelPosition.X - 29);
            Canvas.SetTop(label, labelPosition.Y - 12);
            Fatias.Children.Add(label);
        }
    }

    private static PathGeometry CriarFatia(double x, double y, double inner, double outer, double start, double end)
    {
        var outerStart = Polar(x, y, outer, start);
        var outerEnd = Polar(x, y, outer, end);
        var innerEnd = Polar(x, y, inner, end);
        var innerStart = Polar(x, y, inner, start);
        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outer, outer), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true));
        return new PathGeometry([figure]);
    }

    private static Point Polar(double x, double y, double radius, double degrees)
    {
        var radians = (degrees - 90) * Math.PI / 180;
        return new Point(x + radius * Math.Cos(radians), y + radius * Math.Sin(radians));
    }

    private void Seletor_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => Fatias.Visibility = Visibility.Visible;

    private void Seletor_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!MenuFixado) Fatias.Visibility = Visibility.Collapsed;
    }

    private void HoraAtual_Click(object sender, RoutedEventArgs e)
    {
        MenuFixado = HoraAtual.IsChecked == true;
        Fatias.Visibility = MenuFixado || IsMouseOver ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Fatia_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Path { Tag: int minutes } path) return;
        path.SetResourceReference(Shape.FillProperty, minutes > 0 ? "AccentBrush" : "SidebarHoverBrush");
        path.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
    }

    private void Fatia_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Path { Tag: int minutes } path) return;
        path.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
        path.SetResourceReference(Shape.StrokeProperty, "BackgroundBrush");
    }

    private void Fatia_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Path { Tag: int minutes } || !TimeSpan.TryParse(Time, CultureInfo.InvariantCulture, out var current)) return;
        var newMinutes = Math.Clamp((int)current.TotalMinutes + minutes, 0, 23 * 60 + 59);
        Time = TimeSpan.FromMinutes(newMinutes).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        e.Handled = true;
    }
}
