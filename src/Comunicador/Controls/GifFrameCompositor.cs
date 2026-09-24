using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Comunicador.Controls;

/// <summary>
/// GIF frames can contain only a changed rectangle. Keep one logical-screen bitmap
/// and apply each rectangle at its encoded offset instead of displaying it alone.
/// </summary>
internal sealed class GifFrameCompositor
{
    private const int MaxCanvasPixels = 16_000_000;

    private readonly List<GifFrame> _frames;
    private readonly byte[] _pixels;
    private readonly byte[] _backgroundPixel = new byte[4];
    private byte[] _scratch = [];
    private byte[]? _restoreSnapshot;
    private int _frameIndex = -1;

    internal WriteableBitmap Bitmap { get; }
    internal int FrameCount => _frames.Count;
    internal TimeSpan CurrentDelay => _frames[_frameIndex].Delay;

    internal GifFrameCompositor(GifBitmapDecoder decoder)
    {
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("GIF sem quadros.");

        var metadata = decoder.Metadata as BitmapMetadata;
        var width = ReadNumber(metadata, "/logscrdesc/Width", decoder.Frames[0].PixelWidth);
        var height = ReadNumber(metadata, "/logscrdesc/Height", decoder.Frames[0].PixelHeight);
        if (width <= 0 || height <= 0 || (long)width * height > MaxCanvasPixels)
            throw new InvalidDataException("Dimensoes do GIF invalidas ou excessivas.");

        Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _pixels = new byte[checked(width * height * 4)];
        _frames = new List<GifFrame>(decoder.Frames.Count);

        var backgroundIndex = ReadNumber(metadata, "/logscrdesc/BackgroundColorIndex", -1);
        var backgroundColors = decoder.Palette?.Colors;
        if (backgroundIndex >= 0 && backgroundColors is not null && backgroundIndex < backgroundColors.Count)
        {
            var color = backgroundColors[backgroundIndex];
            _backgroundPixel[0] = color.B;
            _backgroundPixel[1] = color.G;
            _backgroundPixel[2] = color.R;
            _backgroundPixel[3] = color.A;
        }

        foreach (var frame in decoder.Frames)
        {
            var frameMetadata = frame.Metadata as BitmapMetadata;
            var left = ReadNumber(frameMetadata, "/imgdesc/Left", 0);
            var top = ReadNumber(frameMetadata, "/imgdesc/Top", 0);
            if (left < 0 || top < 0 || (long)left + frame.PixelWidth > width ||
                (long)top + frame.PixelHeight > height)
                throw new InvalidDataException("Quadro do GIF fora da area da imagem.");

            var disposal = ReadNumber(frameMetadata, "/grctlext/Disposal", 0);
            var delay = ReadNumber(frameMetadata, "/grctlext/Delay", 10);
            _frames.Add(new GifFrame(frame, left, top, disposal,
                TimeSpan.FromMilliseconds(Math.Max(20, delay * 10))));
        }

        // The transparent index can also be the logical screen's background color.
        var firstMetadata = decoder.Frames[0].Metadata as BitmapMetadata;
        if (ReadFlag(firstMetadata, "/grctlext/TransparencyFlag") &&
            backgroundIndex == ReadNumber(firstMetadata, "/grctlext/TransparentColorIndex", -2))
            Array.Clear(_backgroundPixel);

        ShowNextFrame();
    }

    internal void ShowNextFrame()
    {
        if (_frameIndex >= 0)
            DisposeFrame(_frames[_frameIndex]);

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        if (_frameIndex == 0)
            FillCanvasWithBackground();

        var frame = _frames[_frameIndex];
        _restoreSnapshot = frame.Disposal == 3 ? (byte[])_pixels.Clone() : null;
        DrawFrame(frame);
        Bitmap.WritePixels(new Int32Rect(0, 0, Bitmap.PixelWidth, Bitmap.PixelHeight),
            _pixels, Bitmap.PixelWidth * 4, 0);
    }

    private void DisposeFrame(GifFrame frame)
    {
        if (frame.Disposal == 2)
        {
            for (var y = frame.Top; y < frame.Top + frame.Bitmap.PixelHeight; y++)
            {
                for (var x = frame.Left; x < frame.Left + frame.Bitmap.PixelWidth; x++)
                    _backgroundPixel.CopyTo(_pixels, (y * Bitmap.PixelWidth + x) * 4);
            }
        }
        else if (frame.Disposal == 3 && _restoreSnapshot is not null)
        {
            _restoreSnapshot.CopyTo(_pixels, 0);
        }
        _restoreSnapshot = null;
    }

    private void DrawFrame(GifFrame frame)
    {
        var source = new FormatConvertedBitmap(frame.Bitmap, PixelFormats.Bgra32, null, 0);
        var frameWidth = source.PixelWidth;
        var frameHeight = source.PixelHeight;
        var byteCount = checked(frameWidth * frameHeight * 4);
        if (_scratch.Length < byteCount)
            _scratch = new byte[byteCount];
        source.CopyPixels(_scratch, frameWidth * 4, 0);

        for (var y = 0; y < frameHeight; y++)
        {
            var sourceRow = y * frameWidth * 4;
            var destinationRow = ((frame.Top + y) * Bitmap.PixelWidth + frame.Left) * 4;
            for (var x = 0; x < frameWidth; x++)
            {
                var sourceOffset = sourceRow + x * 4;
                if (_scratch[sourceOffset + 3] == 0) continue;
                var destinationOffset = destinationRow + x * 4;
                _pixels[destinationOffset] = _scratch[sourceOffset];
                _pixels[destinationOffset + 1] = _scratch[sourceOffset + 1];
                _pixels[destinationOffset + 2] = _scratch[sourceOffset + 2];
                _pixels[destinationOffset + 3] = _scratch[sourceOffset + 3];
            }
        }
    }

    private void FillCanvasWithBackground()
    {
        for (var offset = 0; offset < _pixels.Length; offset += 4)
            _backgroundPixel.CopyTo(_pixels, offset);
    }

    private static int ReadNumber(BitmapMetadata? metadata, string query, int fallback)
    {
        try
        {
            return metadata?.GetQuery(query) switch
            {
                byte value => value,
                ushort value => value,
                int value => value,
                _ => fallback,
            };
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }

    private static bool ReadFlag(BitmapMetadata? metadata, string query)
    {
        try { return metadata?.GetQuery(query) is true; }
        catch (NotSupportedException) { return false; }
    }

    private sealed record GifFrame(BitmapFrame Bitmap, int Left, int Top, int Disposal, TimeSpan Delay);
}
