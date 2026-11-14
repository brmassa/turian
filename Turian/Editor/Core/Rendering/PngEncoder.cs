namespace Turian.Editor.Core;

/// <summary>Writes <see cref="RasterImage"/>s as 8-bit PNGs with the BCL's zlib, so tools need no imaging library.</summary>
public static class PngEncoder
{
    static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The PNG bytes of <paramref name="image"/>.</summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="alpha">Whether to keep transparency (RGBA) or drop it (RGB).</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] Encode(RasterImage image, bool alpha = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        var channels = alpha ? 4 : 3;
        var rowLength = 1 + (image.Width * channels);
        var raw = new byte[image.Height * rowLength];
        var source = image.Pixels;
        for (var y = 0; y < image.Height; y++)
        {
            var target = y * rowLength;
            raw[target++] = 0; // filter: none
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = ((y * image.Width) + x) * 4;
                var a = source[pixel + 3];
                raw[target++] = Unpremultiply(source[pixel + 2], a, alpha);
                raw[target++] = Unpremultiply(source[pixel + 1], a, alpha);
                raw[target++] = Unpremultiply(source[pixel], a, alpha);
                if (alpha)
                {
                    raw[target++] = a;
                }
            }
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        using var file = new MemoryStream();
        file.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)image.Height);
        header[8] = 8; // bit depth
        header[9] = alpha ? (byte)6 : (byte)2; // truecolor with or without alpha
        WriteChunk(file, "IHDR", header);
        WriteChunk(file, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>Writes <paramref name="image"/> to <paramref name="path"/> as a PNG.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="image">The image to encode.</param>
    /// <param name="alpha">Whether to keep transparency.</param>
    public static void Save(string path, RasterImage image, bool alpha = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Encode(image, alpha));
    }

    // Opaque output keeps the premultiplied colour, which is what a composited frame over black shows.
    static byte Unpremultiply(byte value, byte alpha, bool keepAlpha) =>
        !keepAlpha || alpha is 255 ? value : alpha == 0 ? (byte)0 : (byte)Math.Min(255, ((value * 255) + (alpha / 2)) / alpha);

    static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        stream.Write(buffer);
        Span<byte> typeBytes = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = new System.IO.Hashing.Crc32();
        crc.Append(typeBytes);
        crc.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc.GetCurrentHashAsUInt32());
        stream.Write(buffer);
    }
}
