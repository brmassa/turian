namespace Turian.Engine.Core;

/// <summary>
/// Size arithmetic for the texture formats the engine uploads. Block-compressed formats are
/// measured in 4×4 blocks rather than texels, so a mip level's byte count is the block count
/// rounded up, never a row pitch.
/// </summary>
public static class TextureFormats
{
    const uint blockExtent = 4;

    /// <summary>
    /// Reports whether <paramref name="format"/> stores 4×4 block-compressed data.
    /// </summary>
    /// <param name="format">The format to classify.</param>
    public static bool IsBlockCompressed(Ktx2VkFormat format) => format switch
    {
        Ktx2VkFormat.Bc1RgbUnormBlock or Ktx2VkFormat.Bc1RgbSrgbBlock
            or Ktx2VkFormat.Bc1RgbaUnormBlock or Ktx2VkFormat.Bc1RgbaSrgbBlock
            or Ktx2VkFormat.Bc2UnormBlock or Ktx2VkFormat.Bc2SrgbBlock
            or Ktx2VkFormat.Bc3UnormBlock or Ktx2VkFormat.Bc3SrgbBlock
            or Ktx2VkFormat.Bc4UnormBlock or Ktx2VkFormat.Bc4SnormBlock
            or Ktx2VkFormat.Bc5UnormBlock or Ktx2VkFormat.Bc5SnormBlock
            or Ktx2VkFormat.Bc7UnormBlock or Ktx2VkFormat.Bc7SrgbBlock => true,
        _ => false,
    };

    /// <summary>
    /// Bytes occupied by one element of <paramref name="format"/> — one 4×4 block for compressed
    /// formats, one texel otherwise.
    /// </summary>
    /// <param name="format">The format to measure.</param>
    /// <exception cref="NotSupportedException">The format is not one the engine uploads.</exception>
    public static uint ElementSizeBytes(Ktx2VkFormat format) => format switch
    {
        Ktx2VkFormat.Bc1RgbUnormBlock or Ktx2VkFormat.Bc1RgbSrgbBlock
            or Ktx2VkFormat.Bc1RgbaUnormBlock or Ktx2VkFormat.Bc1RgbaSrgbBlock
            or Ktx2VkFormat.Bc4UnormBlock or Ktx2VkFormat.Bc4SnormBlock => 8,
        Ktx2VkFormat.Bc2UnormBlock or Ktx2VkFormat.Bc2SrgbBlock
            or Ktx2VkFormat.Bc3UnormBlock or Ktx2VkFormat.Bc3SrgbBlock
            or Ktx2VkFormat.Bc5UnormBlock or Ktx2VkFormat.Bc5SnormBlock
            or Ktx2VkFormat.Bc7UnormBlock or Ktx2VkFormat.Bc7SrgbBlock => 16,
        Ktx2VkFormat.R8G8B8A8Unorm or Ktx2VkFormat.R8G8B8A8Srgb => 4,
        Ktx2VkFormat.R8Unorm => 1,
        Ktx2VkFormat.R8G8Unorm => 2,
        _ => throw new NotSupportedException($"Unsupported texture format: {format}"),
    };

    /// <summary>
    /// Bytes occupied by a tightly-packed <paramref name="width"/>×<paramref name="height"/> mip
    /// level of <paramref name="format"/>, rounding compressed extents up to whole 4×4 blocks.
    /// </summary>
    /// <param name="format">The format of the level.</param>
    /// <param name="width">Level width in texels.</param>
    /// <param name="height">Level height in texels.</param>
    public static uint LevelSizeBytes(Ktx2VkFormat format, uint width, uint height)
    {
        var elementSize = ElementSizeBytes(format);
        if (!IsBlockCompressed(format))
        {
            return width * height * elementSize;
        }

        var blocksWide = Math.Max(1u, (width + blockExtent - 1) / blockExtent);
        var blocksHigh = Math.Max(1u, (height + blockExtent - 1) / blockExtent);
        return blocksWide * blocksHigh * elementSize;
    }

    /// <summary>
    /// Extent of mip level <paramref name="level"/> of a <paramref name="width"/>×
    /// <paramref name="height"/> base image, never smaller than 1×1.
    /// </summary>
    /// <param name="width">Base level width.</param>
    /// <param name="height">Base level height.</param>
    /// <param name="level">Zero-based mip level.</param>
    public static (uint Width, uint Height) LevelExtent(uint width, uint height, int level) =>
        (Math.Max(1u, width >> level), Math.Max(1u, height >> level));

    /// <summary>
    /// Number of mip levels in a complete chain down to 1×1 for the given base extent.
    /// </summary>
    /// <param name="width">Base level width.</param>
    /// <param name="height">Base level height.</param>
    public static uint FullMipChainLength(uint width, uint height) =>
        (uint)(BitOperations.Log2(Math.Max(1u, Math.Max(width, height))) + 1);
}
