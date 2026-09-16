using System.Windows;
using System.Windows.Controls;

namespace Comunicador.Controls;

/// <summary>
/// Distribui cartões em colunas responsivas e sempre coloca o próximo cartão na
/// coluna mais curta. Assim a tela usa toda a largura disponível sem criar os
/// grandes vazios típicos de uma grade com linhas rígidas.
/// </summary>
public sealed class ResponsiveColumnsPanel : Panel
{
    private readonly Dictionary<UIElement, Rect> _layout = new();

    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(390d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnGapProperty = DependencyProperty.Register(
        nameof(ColumnGap), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan", typeof(int), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public static int GetColumnSpan(DependencyObject element) => (int)element.GetValue(ColumnSpanProperty);
    public static void SetColumnSpan(DependencyObject element, int value) => element.SetValue(ColumnSpanProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        _layout.Clear();
        var width = ResolveWidth(availableSize.Width);
        var gap = Math.Max(0, ColumnGap);
        var rowGap = Math.Max(0, RowGap);
        var minWidth = Math.Max(180, MinColumnWidth);
        var maxColumns = Math.Max(1, MaxColumns);
        var columns = Math.Clamp((int)Math.Floor((width + gap) / (minWidth + gap)), 1, maxColumns);
        var columnWidth = Math.Max(1, (width - gap * (columns - 1)) / columns);
        var heights = new double[columns];

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Measure(new Size(0, 0));
                continue;
            }

            var span = Math.Clamp(GetColumnSpan(child), 1, columns);
            if (span == columns)
            {
                var y = heights.Max();
                child.Measure(new Size(width, double.PositiveInfinity));
                var height = child.DesiredSize.Height;
                _layout[child] = new Rect(0, y, width, height);
                var next = y + height + rowGap;
                Array.Fill(heights, next);
                continue;
            }

            var bestColumn = 0;
            var bestY = double.PositiveInfinity;
            for (var start = 0; start <= columns - span; start++)
            {
                var candidateY = heights.Skip(start).Take(span).Max();
                if (candidateY < bestY)
                {
                    bestY = candidateY;
                    bestColumn = start;
                }
            }

            var childWidth = columnWidth * span + gap * (span - 1);
            child.Measure(new Size(childWidth, double.PositiveInfinity));
            var childHeight = child.DesiredSize.Height;
            _layout[child] = new Rect(bestColumn * (columnWidth + gap), bestY, childWidth, childHeight);
            var newHeight = bestY + childHeight + rowGap;
            for (var column = bestColumn; column < bestColumn + span; column++) heights[column] = newHeight;
        }

        var desiredHeight = heights.Length == 0 ? 0 : Math.Max(0, heights.Max() - rowGap);
        return new Size(width, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(_layout.TryGetValue(child, out var rect) ? rect : Rect.Empty);
        }
        return finalSize;
    }

    private double ResolveWidth(double availableWidth)
    {
        if (!double.IsInfinity(availableWidth) && availableWidth > 0) return availableWidth;
        if (ActualWidth > 0) return ActualWidth;
        return Math.Max(MinColumnWidth, 900);
    }
}
