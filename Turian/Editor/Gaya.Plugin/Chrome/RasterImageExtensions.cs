namespace Gaya.Plugin.Turian;

/// <summary>Turns the editor's GUI-free images into images the Studio's toolkit draws.</summary>
static class RasterImageExtensions
{
    /// <summary>A copy of <paramref name="image"/> as a Skia image the caller disposes.</summary>
    /// <param name="image">Premultiplied BGRA pixels.</param>
    /// <returns>The Skia image.</returns>
    public static SKImage ToSkImage(this RasterImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), info.RowBytes);
            return SKImage.FromPixelCopy(pixmap);
        }
        finally
        {
            handle.Free();
        }
    }
}
