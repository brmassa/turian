namespace Turian.Tests.Editor;

/// <summary>
/// Covers the project icon: the image the player settings name is found by its asset id, and a build gets
/// a PNG for the window and a multi-size ICO for the executable — or neither when there is no icon.
/// </summary>
public class ProjectIconTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), $"TurianIcon_{Guid.NewGuid():N}");
    readonly string output;

    /// <summary>Creates an empty project folder.</summary>
    public ProjectIconTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        output = Path.Combine(root, ".Cache");
    }

    /// <summary>Removes the project folder.</summary>
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    static RasterImage Image(int width, int height)
    {
        var image = new RasterImage(width, height);
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            image.Pixels[i + 1] = 0x45;
            image.Pixels[i + 2] = 0xFF;
            image.Pixels[i + 3] = 0xFF;
        }

        return image;
    }

    AppSettings ProjectWithIcon()
    {
        var imagePath = Path.Combine(root, "Assets", "logo.png");
        File.WriteAllBytes(imagePath, PngEncoder.Encode(Image(300, 150)));

        var texture = new TextureAsset { Id = Guid.NewGuid(), RelativePath = "Assets/logo.png" };
        Serializer.Save<Asset>($"{imagePath}.meta", texture);

        var project = SettingsService.Load(root)!;
        ProjectSettingsFiles.Create(project, new PlayerSettings { Icon = new AssetReference<TextureAsset>(texture.Id) });
        return project;
    }

    /// <summary>The icon file is found by the id the player settings hold, even for a project that is not open.</summary>
    [Fact]
    public void TheIconIsFoundByItsAssetId()
    {
        ProjectWithIcon();

        Assert.Equal(Path.Combine(root, "Assets", "logo.png"), ProjectIcon.FindSource(root));
    }

    /// <summary>A build gets a square PNG no larger than 256 pixels and an ICO beside it.</summary>
    [Fact]
    public void ABuildGetsASquarePngAndAnIco()
    {
        Assert.True(ProjectIcon.Write(ProjectWithIcon(), output));

        using var png = SKBitmap.Decode(File.ReadAllBytes(Path.Combine(output, ProjectIcon.PngFileName)));
        Assert.Equal(256, png.Width);
        Assert.Equal(256, png.Height);
        Assert.Equal(new SKColor(0xFF, 0x45, 0x00), png.GetPixel(128, 128));
        Assert.Equal(0, png.GetPixel(128, 10).Alpha);
        Assert.True(File.Exists(Path.Combine(output, ProjectIcon.IcoFileName)));
    }

    /// <summary>An <c>.ico</c> source is read from its largest PNG entry; one without PNG entries is no icon.</summary>
    [Fact]
    public void AnIcoSourceUsesItsLargestPngEntry()
    {
        var ico = Path.Combine(root, "logo.ico");
        File.WriteAllBytes(ico, ProjectIcon.EncodeIco(Image(64, 32)));
        var decoded = ProjectIcon.Decode(ico);
        Assert.NotNull(decoded);
        Assert.Equal(256, decoded.Width);

        File.WriteAllBytes(ico, [0, 0, 1, 0, 1, 0, .. new byte[16]]);
        Assert.Null(ProjectIcon.Decode(ico));
        Assert.Null(ProjectIcon.Decode(Path.Combine(root, "missing.png")));
    }

    /// <summary>A project without an icon leaves no icon files behind from an earlier build.</summary>
    [Fact]
    public void AProjectWithoutAnIconRemovesStaleIconFiles()
    {
        ProjectIcon.Write(ProjectWithIcon(), output);
        var project = SettingsService.Load(root)!;
        project.Loaded.Use(new PlayerSettings());

        Assert.False(ProjectIcon.Write(project, output));
        Assert.False(File.Exists(Path.Combine(output, ProjectIcon.PngFileName)));
        Assert.False(File.Exists(Path.Combine(output, ProjectIcon.IcoFileName)));
    }

    /// <summary>The ICO directory lists every size, each entry pointing at a PNG of that size.</summary>
    [Fact]
    public void TheIcoHoldsAPngPerSize()
    {
        var ico = ProjectIcon.EncodeIco(Image(64, 64));

        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        var count = BitConverter.ToUInt16(ico, 4);
        Assert.Equal(4, count);

        for (var entry = 0; entry < count; entry++)
        {
            var header = 6 + (16 * entry);
            var size = ico[header] == 0 ? 256 : ico[header];
            var length = BitConverter.ToInt32(ico, header + 8);
            var offset = BitConverter.ToInt32(ico, header + 12);

            using var png = SKBitmap.Decode(ico.AsSpan(offset, length).ToArray());
            Assert.Equal(size, png.Width);
        }
    }
}
