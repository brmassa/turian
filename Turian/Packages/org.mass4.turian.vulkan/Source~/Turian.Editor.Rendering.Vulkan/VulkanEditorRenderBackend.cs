[assembly: Turian.Editor.Core.EditorRenderBackend(typeof(Turian.Editor.Core.VulkanEditorRenderBackend))]

namespace Turian.Editor.Core;

/// <summary>Gives the Studio and CLI the Vulkan device, offscreen captures and asset thumbnails.</summary>
public sealed class VulkanEditorRenderBackend : IEditorRenderBackend
{
    /// <inheritdoc />
    public string Id => "vulkan";

    /// <inheritdoc />
    public ulong UploadedTextureBytes => TextureAsset.UploadedBytes;

    /// <inheritdoc />
    public void ReleaseCachedModels() => ModelAsset.ClearCache();

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(static sp => new Vulkan(sp.GetRequiredService<ILogger>(), sp.GetService<IAppSettings>()));
        services.TryAddSingleton<IRenderDevice>(static sp => sp.GetRequiredService<Vulkan>());
    }

    /// <inheritdoc />
    public IRenderDevice CreateDevice(ILogger logger) => new Vulkan(logger);

    /// <inheritdoc />
    public ISceneCapture CreateCapture(IRenderDevice device, AssetDatabase assets, SceneCaptureOptions options) =>
        new VulkanSceneCapture(Device(device), assets, options);

    /// <inheritdoc />
    public IAssetThumbnailRenderer CreateThumbnailRenderer(IRenderDevice device, AssetDatabase assets,
        AssetPreviewCatalog catalog) => new VulkanThumbnailRenderer(Device(device), assets, catalog);

    static Vulkan Device(IRenderDevice device) => device as Vulkan
        ?? throw new ArgumentException($"The Vulkan backend cannot render with a {device?.GetType().Name ?? "missing"} device.",
            nameof(device));
}
