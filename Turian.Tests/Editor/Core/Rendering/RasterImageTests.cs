namespace Turian.Tests;

/// <summary>Covers the GUI-free image tools use: decoding, resampling, fitting and PNG output.</summary>
public sealed class RasterImageTests
{
    static RasterImage Solid(int width, int height, byte b, byte g, byte r, byte a)
    {
        var image = new RasterImage(width, height);
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]) = (b, g, r, a);
        }

        return image;
    }

    /// <summary>A PNG keeps size, color and translucency through an encode and decode, premultiplied on both ends.</summary>
    [Fact]
    public void PngRoundTripKeepsPremultipliedPixels()
    {
        var source = Solid(3, 2, 0x20, 0x40, 0x80, 0x80);

        var decoded = RasterImage.Decode(PngEncoder.Encode(source));

        Assert.NotNull(decoded);
        Assert.Equal((3, 2), (decoded.Width, decoded.Height));
        Assert.All(Enumerable.Range(0, 4), c => Assert.InRange(decoded.Pixels[c] - source.Pixels[c], -1, 1));
    }

    /// <summary>Another decoder reads the opaque output as RGB with the composited color.</summary>
    [Fact]
    public void OpaquePngIsReadableElsewhere()
    {
        using var bitmap = SKBitmap.Decode(PngEncoder.Encode(Solid(2, 2, 0x00, 0x45, 0xFF, 0xFF), alpha: false));

        Assert.Equal(new SKColor(0xFF, 0x45, 0x00), bitmap.GetPixel(1, 1));
    }

    /// <summary>Shrinking averages every covered pixel, and fitting keeps the aspect within the bound.</summary>
    [Fact]
    public void ShrinkingAveragesAndFittingKeepsAspect()
    {
        var stripes = new RasterImage(4, 1);
        for (var x = 0; x < 4; x++)
        {
            stripes.Pixels[(x * 4) + 2] = x % 2 == 0 ? (byte)255 : (byte)0;
            stripes.Pixels[(x * 4) + 3] = 255;
        }

        var half = stripes.Resize(2, 1);
        Assert.All([half.Pixels[2], half.Pixels[6]], static red => Assert.InRange(red, 100, 155));

        var fitted = Solid(400, 200, 0, 0, 255, 255).FitWithin(128);
        Assert.Equal((128, 64), (fitted.Width, fitted.Height));
        var small = Solid(10, 5, 0, 0, 0, 255).FitWithin(64);
        Assert.Equal((10, 5), (small.Width, small.Height));
    }

    /// <summary>Fitting into a square letterboxes with transparency and keeps the image centered.</summary>
    [Fact]
    public void FittingASquareLetterboxes()
    {
        var square = Solid(4, 2, 0, 0, 255, 255).FitSquare(8);

        Assert.Equal(0, square.Pixels[3]);
        Assert.Equal(255, square.Pixels[(((4 * 8) + 4) * 4) + 3]);
    }

    /// <summary>Unreadable data and missing files decode to nothing instead of throwing; bad sizes are rejected.</summary>
    [Fact]
    public void UnreadableInputsDecodeToNothing()
    {
        Assert.Null(RasterImage.Decode([1, 2, 3]));
        Assert.Null(RasterImage.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png")));
        Assert.Throws<ArgumentOutOfRangeException>(static () => new RasterImage(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(static () => new RasterImage(1, 1, new byte[3]));
    }
}
