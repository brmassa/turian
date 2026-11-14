namespace Turian.Editor.Core;

/// <summary>
/// A project's icon: finding the image <see cref="PlayerSettings.Icon"/> names, and turning it into the
/// files a build ships — <see cref="PngFileName"/> for the window and <see cref="IcoFileName"/> for the
/// Windows executable.
/// </summary>
public static class ProjectIcon
{
    /// <summary>The icon a built game loads for its window, beside the executable.</summary>
    public const string PngFileName = PlayerSettings.BuiltIconFileName;

    /// <summary>The icon the Windows executable is stamped with.</summary>
    public const string IcoFileName = "icon.ico";

    static readonly int[] IcoSizes = [16, 32, 48, 256];

    static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tga", ".ico"];

    /// <summary>
    /// The source image of a project's icon, for a project that is not open: its player settings are read
    /// from its sources.
    /// </summary>
    /// <param name="projectDirectory">The project folder.</param>
    /// <returns>Absolute path of the image, or null when the project names none or it is missing.</returns>
    public static string? FindSource(string projectDirectory)
    {
        if (SettingsService.Load(projectDirectory) is not { } project)
        {
            return null;
        }

        ProjectSettingsLoader.LoadFromSources(project);
        return FindSource(project);
    }

    /// <summary>The source image of an open project's icon.</summary>
    /// <param name="project">The project.</param>
    /// <returns>Absolute path of the image, or null when the project names none or it is missing.</returns>
    public static string? FindSource(IAppSettings project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Get<PlayerSettings>().Icon is not { IsEmpty: false } icon)
        {
            return null;
        }

        if (!Directory.Exists(project.AssetsAbsoluteDir))
        {
            return null;
        }

        return Directory.EnumerateFiles(project.AssetsAbsoluteDir, "*.meta", SearchOption.AllDirectories)
            .Select(static meta => meta[..^".meta".Length])
            .Where(static source =>
                ImageExtensions.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
            .FirstOrDefault(source => File.Exists(source) && MetaId(source) == icon.AssetId);
    }

    /// <summary>
    /// Writes <see cref="PngFileName"/> and <see cref="IcoFileName"/> for a project into a folder, or removes
    /// them when the project has no icon, so a build never ships a stale one.
    /// </summary>
    /// <param name="project">The project being built.</param>
    /// <param name="outputDirectory">Where the icon files go.</param>
    /// <returns>True when icon files were written.</returns>
    public static bool Write(IAppSettings project, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        var pngPath = Path.Combine(outputDirectory, PngFileName);
        var icoPath = Path.Combine(outputDirectory, IcoFileName);

        var image = FindSource(project) is { } source ? Decode(source) : null;
        if (image is null)
        {
            File.Delete(pngPath);
            File.Delete(icoPath);
            return false;
        }

        File.WriteAllBytes(pngPath, EncodePng(image, Math.Min(256, Math.Max(image.Width, image.Height))));
        File.WriteAllBytes(icoPath, EncodeIco(image));
        return true;
    }

    /// <summary>
    /// An <c>.ico</c> holding PNG-compressed square images at the usual sizes, which every Windows since
    /// Vista reads.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] EncodeIco(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var entries = IcoSizes.Select(size => (Size: size, Png: EncodePng(image, size))).ToList();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)entries.Count);

        var offset = 6 + (16 * entries.Count);
        foreach (var (size, png) in entries)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in entries)
        {
            writer.Write(png);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>The image fitted, centered and letterboxed, into a transparent square, as PNG.</summary>
    /// <param name="image">The source image.</param>
    /// <param name="size">The square's side in pixels.</param>
    /// <returns>The PNG's bytes.</returns>
    public static byte[] EncodePng(RasterImage image, int size)
    {
        ArgumentNullException.ThrowIfNull(image);
        return PngEncoder.Encode(image.FitSquare(size));
    }

    /// <summary>
    /// Decodes an icon source: any image <see cref="RasterImage.Decode"/> reads, or the largest PNG entry of an
    /// <c>.ico</c>. Returns <c>null</c> for anything else, such as an <c>.ico</c> holding only bitmaps.
    /// </summary>
    /// <param name="path">The image file.</param>
    /// <returns>The image, or <c>null</c>.</returns>
    public static RasterImage? Decode(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        return Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase)
            ? LargestIcoPng(bytes) is { } png ? RasterImage.Decode(png) : null
            : RasterImage.Decode(bytes);
    }

    // ICONDIR, then 16-byte entries whose last two fields are the image's length and offset.
    static byte[]? LargestIcoPng(byte[] ico)
    {
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)) != 1)
        {
            return null;
        }

        byte[]? largest = null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (var i = 0; i < count && 6 + ((i + 1) * 16) <= ico.Length; i++)
        {
            var entry = ico.AsSpan(6 + (i * 16), 16);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            if (length <= 8 || offset < 0 || offset > ico.Length - length
                || !ico.AsSpan(offset, 8).SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
                || length <= (largest?.Length ?? 0))
            {
                continue;
            }

            largest = ico.AsSpan(offset, length).ToArray();
        }

        return largest;
    }

    static Guid MetaId(string sourcePath)
    {
        try
        {
            return Asset.Load($"{sourcePath}.meta")?.Id ?? Guid.Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return Guid.Empty;
        }
    }
}
