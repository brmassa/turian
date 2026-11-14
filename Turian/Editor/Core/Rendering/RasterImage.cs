namespace Turian.Editor.Core;

/// <summary>
/// A CPU image in 8-bit BGRA with premultiplied alpha, rows top to bottom: the format renderers read back, so tools
/// exchange pixels without a GUI or graphics library.
/// </summary>
public sealed class RasterImage
{
    /// <summary>Wraps or allocates <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
    /// <param name="width">Width in pixels, at least one.</param>
    /// <param name="height">Height in pixels, at least one.</param>
    /// <param name="pixels">Existing BGRA pixels of exactly that size, or <c>null</c> for a transparent image.</param>
    public RasterImage(int width, int height, byte[]? pixels = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        pixels ??= new byte[width * height * 4];
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, width * height * 4, nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The BGRA bytes, premultiplied, <see cref="Width"/> × 4 bytes per row.</summary>
    public byte[] Pixels { get; }

    /// <summary>Decodes a PNG, JPEG, BMP, TGA, GIF or PSD image, or <c>null</c> when the data is none of them.</summary>
    /// <param name="encoded">The file's bytes.</param>
    /// <returns>The image, or <c>null</c>.</returns>
    public static RasterImage? Decode(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ImageResult decoded;
        try
        {
            decoded = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }

        if (decoded.Width <= 0 || decoded.Height <= 0)
        {
            return null;
        }

        var pixels = decoded.Data;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            var red = pixels[i];
            pixels[i] = Premultiply(pixels[i + 2], alpha);
            pixels[i + 1] = Premultiply(pixels[i + 1], alpha);
            pixels[i + 2] = Premultiply(red, alpha);
        }

        return new RasterImage(decoded.Width, decoded.Height, pixels);
    }

    /// <summary>Decodes the image file at <paramref name="path"/>, or <c>null</c> when it is missing or unreadable.</summary>
    /// <param name="path">The image file.</param>
    /// <returns>The image, or <c>null</c>.</returns>
    public static RasterImage? Load(string path)
    {
        try
        {
            // Reading the bytes keeps the file from staying open, which blocks deleting it on Windows.
            return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>This image resampled to <paramref name="width"/> × <paramref name="height"/>.</summary>
    /// <param name="width">The new width, at least one.</param>
    /// <param name="height">The new height, at least one.</param>
    /// <returns>A new image; averaging when shrinking, interpolating when enlarging.</returns>
    public RasterImage Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return new RasterImage(width, height, (byte[])Pixels.Clone());
        }

        var horizontal = Resample(Pixels, Width, Height, width, horizontal: true);
        return new RasterImage(width, height, Resample(horizontal, width, Height, height, horizontal: false));
    }

    /// <summary>This image scaled down, keeping its aspect, so neither side exceeds <paramref name="size"/>.</summary>
    /// <param name="size">The longest side allowed.</param>
    /// <returns>A new image, unscaled when it already fits.</returns>
    public RasterImage FitWithin(int size)
    {
        var scale = Math.Min(1f, size / (float)Math.Max(Width, Height));
        return Resize(Math.Max(1, (int)(Width * scale)), Math.Max(1, (int)(Height * scale)));
    }

    /// <summary>This image fitted, centered and letterboxed, into a transparent <paramref name="size"/> square.</summary>
    /// <param name="size">The square's side in pixels.</param>
    /// <returns>A new square image.</returns>
    public RasterImage FitSquare(int size)
    {
        var scale = (float)size / Math.Max(Width, Height);
        var fitted = Resize(Math.Max(1, (int)MathF.Round(Width * scale)), Math.Max(1, (int)MathF.Round(Height * scale)));
        var square = new RasterImage(size, size);
        var left = (size - fitted.Width) / 2;
        var top = (size - fitted.Height) / 2;
        for (var y = 0; y < fitted.Height; y++)
        {
            fitted.Pixels.AsSpan(y * fitted.Width * 4, fitted.Width * 4)
                .CopyTo(square.Pixels.AsSpan((((top + y) * size) + left) * 4));
        }

        return square;
    }

    static byte Premultiply(byte value, byte alpha) => (byte)(((value * alpha) + 127) / 255);

    // One separable pass with a tent filter widened to the scale, so shrinking averages every covered source pixel.
    static byte[] Resample(byte[] source, int width, int height, int target, bool horizontal)
    {
        var sourceLength = horizontal ? width : height;
        var result = new byte[(horizontal ? target * height : width * target) * 4];
        var scale = (float)sourceLength / target;
        var radius = Math.Max(1f, scale);
        Span<float> sum = stackalloc float[4];
        for (var t = 0; t < target; t++)
        {
            var center = ((t + 0.5f) * scale) - 0.5f;
            var first = Math.Max(0, (int)MathF.Floor(center - radius));
            var last = Math.Min(sourceLength - 1, (int)MathF.Ceiling(center + radius));
            var lines = horizontal ? height : width;
            for (var line = 0; line < lines; line++)
            {
                sum.Clear();
                var total = 0f;
                for (var s = first; s <= last; s++)
                {
                    var weight = 1f - (MathF.Abs(s - center) / radius);
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    var index = (horizontal ? (line * width) + s : (s * width) + line) * 4;
                    for (var c = 0; c < 4; c++)
                    {
                        sum[c] += source[index + c] * weight;
                    }

                    total += weight;
                }

                var output = (horizontal ? (line * target) + t : (t * width) + line) * 4;
                for (var c = 0; c < 4; c++)
                {
                    result[output + c] = (byte)Math.Clamp((int)MathF.Round(sum[c] / total), 0, 255);
                }
            }
        }

        return result;
    }
}
