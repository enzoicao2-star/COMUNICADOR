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
    private readonly List<BitmapSource> _frames = [];
    private readonly List<TimeSpan> _durations = [];
    private int _frameIndex;

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

    public AnimatedImage()
    {
        _timer.Tick += (_, _) => AdvanceFrame();
        Loaded += (_, _) => StartIfAnimated();
        Unloaded += (_, _) => _timer.Stop();
    }

    private static void OnImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedImage image) image.LoadImage();
    }

    private void LoadImage()
    {
        _timer.Stop();
        _frames.Clear();
        _durations.Clear();
        _frameIndex = 0;
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
                foreach (var frame in decoder.Frames)
                {
                    frame.Freeze();
                    _frames.Add(frame);
                    _durations.Add(ReadDelay(frame));
                }
            }
            else
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                _frames.Add(bitmap);
                _durations.Add(TimeSpan.Zero);
            }
            if (_frames.Count > 0) Source = _frames[0];
            StartIfAnimated();
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
        {
            Source = null;
        }
    }

    private static TimeSpan ReadDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata &&
                metadata.GetQuery("/grctlext/Delay") is ushort hundredths)
                return TimeSpan.FromMilliseconds(Math.Max(20, hundredths * 10));
        }
        catch (NotSupportedException) { }
        return TimeSpan.FromMilliseconds(100);
    }

    private void StartIfAnimated()
    {
        if (!IsLoaded || _frames.Count <= 1) return;
        _timer.Interval = _durations[0];
        _timer.Start();
    }

    private void AdvanceFrame()
    {
        if (_frames.Count <= 1) { _timer.Stop(); return; }
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        Source = _frames[_frameIndex];
        _timer.Interval = _durations[_frameIndex];
    }
}
