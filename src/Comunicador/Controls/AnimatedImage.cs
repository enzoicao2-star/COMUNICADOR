using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Comunicador.Controls;

/// <summary>Exibe imagens comuns e preserva a animação de GIFs recebidos.</summary>
public sealed class AnimatedImage : Image
{
    private readonly DispatcherTimer _timer = new();
    private GifFrameCompositor? _gif;

    public static readonly DependencyProperty DataBase64Property = DependencyProperty.Register(
        nameof(DataBase64), typeof(string), typeof(AnimatedImage),
        new PropertyMetadata(null, OnImageChanged));
    public static readonly DependencyProperty MimeTypeProperty = DependencyProperty.Register(
        nameof(MimeType), typeof(string), typeof(AnimatedImage),
        new PropertyMetadata(null, OnImageChanged));

    public string? DataBase64
    {
        get => (string?)GetValue(DataBase64Property);
        set => SetValue(DataBase64Property, value);
    }

    public string? MimeType
    {
        get => (string?)GetValue(MimeTypeProperty);
        set => SetValue(MimeTypeProperty, value);
    }

    internal bool IsPlaybackTimerEnabled => _timer.IsEnabled;

    public AnimatedImage()
    {
        _timer.Tick += (_, _) => AdvanceFrame();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        IsVisibleChanged += OnIsVisibleChanged;
        StartIfAnimated();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        IsVisibleChanged -= OnIsVisibleChanged;
        _timer.Stop();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) StartIfAnimated();
        else _timer.Stop();
    }

    private static void OnImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedImage image) image.LoadImage();
    }

    private void LoadImage()
    {
        _timer.Stop();
        _gif = null;
        Source = null;
        if (string.IsNullOrWhiteSpace(DataBase64)) return;

        try
        {
            var bytes = Convert.FromBase64String(DataBase64);
            using var stream = new MemoryStream(bytes, writable: false);
            if (string.Equals(MimeType, "image/gif", StringComparison.OrdinalIgnoreCase))
            {
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                _gif = new GifFrameCompositor(decoder);
                Source = _gif.Bitmap;
            }
            else
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                Source = bitmap;
            }
            StartIfAnimated();
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException or OverflowException)
        {
            Source = null;
            _gif = null;
        }
    }

    private void StartIfAnimated()
    {
        if (!IsLoaded || !IsVisible || _gif is not { FrameCount: > 1 } gif) return;
        _timer.Interval = gif.CurrentDelay;
        _timer.Start();
    }

    private void AdvanceFrame()
    {
        if (_gif is not { FrameCount: > 1 } gif) { _timer.Stop(); return; }
        gif.ShowNextFrame();
        _timer.Interval = gif.CurrentDelay;
    }
}
