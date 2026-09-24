using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Media.Imaging;
using Comunicador.Controls;
using Xunit;

namespace Comunicador.Tests;

public sealed class GifFrameCompositorTests
{
    // Four-by-four GIFs with optimized partial frames and different disposal modes.
    private const string PartialFrames =
        "R0lGODlhBAAEAIEAAP////8AAAAA/wAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQEBAAAACwAAAAABAAEAAAIDAADABgIQADBgwQDAgAh+QQFBgADACwBAAEAAgABAIH/////AAAAAP8AAAAIBQABCAgIACH5BAUIAAMALAIAAQABAAIAgf////8AAAAA/wAAAAgFAAEICAgAOw==";
    private const string RestoreBackground =
        "R0lGODlhBAAEAIEAAP////8AAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQEBAAAACwAAAAABAAEAAAICwABCBQYYKDBgQEBACH5BAkGAAIALAEAAQACAAEAgf///wAA/wAAAAAAAAgFAAEECAgAIfkEBAgAAAAsAAAAAAQABACB////AIAAAAAAAAAACAsAAQgcSDAAQQABAQA7";
    private const string RestorePrevious =
        "R0lGODlhBAAEAIEAAP////8AAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQEBAAAACwAAAAABAAEAAAICwABCBQYYKDBgQEBACH5BA0GAAIALAEAAQACAAEAgf///wAA/wAAAAAAAAgFAAEECAgAIfkEBQgAAgAsAgABAAEAAgCB////AIAAAAAAAAAACAUAAQQICAA7";

    [Fact]
    public void QuadrosParciais_SaoCompostosNaPosicaoOriginal()
    {
        RunSta(() =>
        {
            var gif = Create(PartialFrames);
            Assert.Equal(3, gif.FrameCount);
            Assert.Equal(4, gif.Bitmap.PixelWidth);
            Assert.Equal(TimeSpan.FromMilliseconds(40), gif.CurrentDelay);
            AssertPixel(gif, 1, 1, 0, 0, 255);

            gif.ShowNextFrame();
            Assert.Equal(TimeSpan.FromMilliseconds(60), gif.CurrentDelay);
            AssertPixel(gif, 1, 1, 255, 255, 255);
            AssertPixel(gif, 2, 1, 0, 0, 255);
            AssertPixel(gif, 0, 0, 255, 0, 0);

            gif.ShowNextFrame();
            Assert.Equal(TimeSpan.FromMilliseconds(80), gif.CurrentDelay);
            AssertPixel(gif, 2, 1, 255, 255, 255);
            AssertPixel(gif, 2, 2, 0, 0, 255);
            AssertPixel(gif, 0, 0, 255, 0, 0);

            gif.ShowNextFrame();
            AssertPixel(gif, 1, 1, 0, 0, 255);
            AssertPixel(gif, 2, 2, 255, 255, 255);
        });
    }

    [Theory]
    [InlineData(RestoreBackground)]
    [InlineData(RestorePrevious)]
    public void DescarteDeQuadro_NaoDeixaPixelsFantasmas(string fixture)
    {
        RunSta(() =>
        {
            var gif = Create(fixture);
            AssertPixel(gif, 1, 1, 255, 0, 0);

            gif.ShowNextFrame();
            AssertPixel(gif, 2, 1, 0, 0, 255);

            gif.ShowNextFrame();
            AssertPixel(gif, 2, 1, 255, 255, 255);
            AssertPixel(gif, 2, 2, 0, 128, 0);
            if (fixture == RestorePrevious)
                AssertPixel(gif, 1, 1, 255, 0, 0);
            else
                AssertPixel(gif, 1, 1, 255, 255, 255);
        });
    }

    private static GifFrameCompositor Create(string fixture)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(fixture));
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return new GifFrameCompositor(decoder);
    }

    private static void AssertPixel(GifFrameCompositor gif, int x, int y, byte red, byte green, byte blue)
    {
        var pixels = new byte[gif.Bitmap.PixelWidth * gif.Bitmap.PixelHeight * 4];
        gif.Bitmap.CopyPixels(pixels, gif.Bitmap.PixelWidth * 4, 0);
        var offset = (y * gif.Bitmap.PixelWidth + x) * 4;
        Assert.Equal(blue, pixels[offset]);
        Assert.Equal(green, pixels[offset + 1]);
        Assert.Equal(red, pixels[offset + 2]);
        Assert.Equal(255, pixels[offset + 3]);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
