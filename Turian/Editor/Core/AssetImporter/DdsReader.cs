namespace Turian.Editor.Core;

/// <summary>
/// Reads DirectDraw Surface files into the block data a Vulkan upload wants. Only the
/// block-compressed families the engine can sample are recognised; nothing is decompressed, so a
/// BC3 file costs one header parse and a set of slices over the caller's buffer.
/// </summary>
public static class DdsReader
{
    /// <summary>File magic, the ASCII bytes <c>DDS </c>.</summary>
    public const uint Magic = 0x20534444;

    const int magicSize = 4;
    const int headerSize = 124;
    const int dxt10HeaderSize = 20;
    const int expectedHeaderSize = 124;

    // Field offsets within DDS_HEADER, relative to the start of the file.
    const int offsetHeaderSize = 4;
    const int offsetHeight = 12;
    const int offsetWidth = 16;
    const int offsetMipMapCount = 28;
    const int offsetPixelFormatFlags = 80;
    const int offsetFourCc = 84;
    const int offsetDxt10 = magicSize + headerSize;

    const uint pixelFormatFourCc = 0x4;

    static uint FourCc(string code) =>
        (uint)(code[0] | (code[1] << 8) | (code[2] << 16) | (code[3] << 24));

    /// <summary>
    /// Reports whether <paramref name="data"/> starts with the DDS magic.
    /// </summary>
    /// <param name="data">Bytes to sniff; may be shorter than a full header.</param>
    public static bool IsDds(ReadOnlySpan<byte> data) =>
        data.Length >= magicSize && BinaryPrimitives.ReadUInt32LittleEndian(data) == Magic;

    /// <summary>
    /// Parses <paramref name="data"/> into its format, extent and mip levels.
    /// </summary>
    /// <param name="data">The whole DDS file.</param>
    /// <param name="isSrgb">
    /// Whether the data is sampled in sRGB space. Selects the sRGB variant for fourCC formats that
    /// have one; BC4 and BC5 store linear data and ignore it, and a DX10 header names its color
    /// space explicitly and wins.
    /// </param>
    /// <returns>The parsed image, with one slice per mip level.</returns>
    /// <exception cref="InvalidDataException">The file is not a DDS the engine can upload.</exception>
    public static DdsImage Read(ReadOnlyMemory<byte> data, bool isSrgb)
    {
        var span = data.Span;
        if (!IsDds(span))
        {
            throw new InvalidDataException("Not a DDS file: missing 'DDS ' magic");
        }

        if (span.Length < magicSize + headerSize)
        {
            throw new InvalidDataException($"DDS file truncated: {span.Length} bytes is shorter than a header");
        }

        var declaredHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(span[offsetHeaderSize..]);
        if (declaredHeaderSize != expectedHeaderSize)
        {
            throw new InvalidDataException($"DDS header size is {declaredHeaderSize}, expected {expectedHeaderSize}");
        }

        var height = BinaryPrimitives.ReadUInt32LittleEndian(span[offsetHeight..]);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(span[offsetWidth..]);

        // DDSD_MIPMAPCOUNT may be unset even when the field is meaningful, and 0 means one level.
        var mipMapCount = Math.Max(1u, BinaryPrimitives.ReadUInt32LittleEndian(span[offsetMipMapCount..]));

        var pixelFormatFlags = BinaryPrimitives.ReadUInt32LittleEndian(span[offsetPixelFormatFlags..]);
        if ((pixelFormatFlags & pixelFormatFourCc) == 0)
        {
            throw new InvalidDataException("Uncompressed DDS files are not supported; expected a fourCC pixel format");
        }

        var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(span[offsetFourCc..]);
        var dataOffset = offsetDxt10;
        Ktx2VkFormat format;

        if (fourCc == FourCc("DX10"))
        {
            if (span.Length < offsetDxt10 + dxt10HeaderSize)
            {
                throw new InvalidDataException("DDS file declares a DX10 header but is too short to hold one");
            }

            var dxgiFormat = BinaryPrimitives.ReadUInt32LittleEndian(span[offsetDxt10..]);
            format = FromDxgiFormat(dxgiFormat);
            dataOffset += dxt10HeaderSize;
        }
        else
        {
            format = FromFourCc(fourCc, isSrgb);
        }

        return new DdsImage(format, width, height, ReadLevels(data, dataOffset, format, width, height, mipMapCount));
    }

    /// <summary>
    /// Slices the payload into mip levels. A chain that runs past the end of the file is truncated
    /// to the levels actually present rather than rejected — DDS writers vary.
    /// </summary>
    static List<ReadOnlyMemory<byte>> ReadLevels(
        ReadOnlyMemory<byte> data,
        int dataOffset,
        Ktx2VkFormat format,
        uint width,
        uint height,
        uint mipMapCount)
    {
        var levels = new List<ReadOnlyMemory<byte>>((int)mipMapCount);
        var offset = dataOffset;

        for (var level = 0; level < mipMapCount; level++)
        {
            var (levelWidth, levelHeight) = TextureFormats.LevelExtent(width, height, level);
            var levelSize = (int)TextureFormats.LevelSizeBytes(format, levelWidth, levelHeight);

            if (offset + levelSize > data.Length)
            {
                if (level == 0)
                {
                    throw new InvalidDataException(
                        $"DDS file truncated: mip 0 needs {levelSize} bytes, only {data.Length - offset} remain");
                }

                Log.Logger.LogWarning(
                    "DDS declares {Declared} mip levels but only {Present} fit in the file; using those",
                    mipMapCount,
                    level);
                break;
            }

            levels.Add(data.Slice(offset, levelSize));
            offset += levelSize;
        }

        return levels;
    }

    static Ktx2VkFormat FromFourCc(uint fourCc, bool isSrgb)
    {
        if (fourCc == FourCc("DXT1"))
        {
            return isSrgb ? Ktx2VkFormat.Bc1RgbaSrgbBlock : Ktx2VkFormat.Bc1RgbaUnormBlock;
        }

        if (fourCc == FourCc("DXT3"))
        {
            return isSrgb ? Ktx2VkFormat.Bc2SrgbBlock : Ktx2VkFormat.Bc2UnormBlock;
        }

        if (fourCc == FourCc("DXT5"))
        {
            return isSrgb ? Ktx2VkFormat.Bc3SrgbBlock : Ktx2VkFormat.Bc3UnormBlock;
        }

        if (fourCc == FourCc("ATI1") || fourCc == FourCc("BC4U"))
        {
            return Ktx2VkFormat.Bc4UnormBlock;
        }

        if (fourCc == FourCc("BC4S"))
        {
            return Ktx2VkFormat.Bc4SnormBlock;
        }

        if (fourCc == FourCc("ATI2") || fourCc == FourCc("BC5U"))
        {
            return Ktx2VkFormat.Bc5UnormBlock;
        }

        if (fourCc == FourCc("BC5S"))
        {
            return Ktx2VkFormat.Bc5SnormBlock;
        }

        var code = new string([(char)(fourCc & 0xFF), (char)((fourCc >> 8) & 0xFF), (char)((fourCc >> 16) & 0xFF), (char)((fourCc >> 24) & 0xFF)]);
        throw new InvalidDataException($"Unsupported DDS fourCC '{code}'");
    }

    static Ktx2VkFormat FromDxgiFormat(uint dxgiFormat) => dxgiFormat switch
    {
        71 => Ktx2VkFormat.Bc1RgbaUnormBlock,
        72 => Ktx2VkFormat.Bc1RgbaSrgbBlock,
        74 => Ktx2VkFormat.Bc2UnormBlock,
        75 => Ktx2VkFormat.Bc2SrgbBlock,
        77 => Ktx2VkFormat.Bc3UnormBlock,
        78 => Ktx2VkFormat.Bc3SrgbBlock,
        80 => Ktx2VkFormat.Bc4UnormBlock,
        81 => Ktx2VkFormat.Bc4SnormBlock,
        83 => Ktx2VkFormat.Bc5UnormBlock,
        84 => Ktx2VkFormat.Bc5SnormBlock,
        98 => Ktx2VkFormat.Bc7UnormBlock,
        99 => Ktx2VkFormat.Bc7SrgbBlock,
        _ => throw new InvalidDataException($"Unsupported DDS DXGI format {dxgiFormat}"),
    };
}
