namespace Turian.Editor.Core;

/// <summary>Renders provider previews through one reusable offscreen viewer.</summary>
public sealed class VulkanThumbnailRenderer(Vulkan vulkan, AssetDatabase assets, AssetPreviewCatalog catalog)
    : IAssetThumbnailRenderer
{
    SceneViewerService? viewer;
    readonly Node emptyRoot = new() { Name = "AssetThumbnail" };

    /// <inheritdoc />
    public RasterImage? Render(AssetEntry entry, int size)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.AssetMetadata is not { } asset)
        {
            return null;
        }

        var provider = catalog.GetProvider(asset.GetType());
        if (provider is null)
        {
            return null;
        }

        if (provider is TextureAssetPreviewProvider && RasterImage.Load(entry.AbsolutePath) is { } decoded)
        {
            return decoded.FitWithin(size);
        }

        EnsureViewer(size);
        return RenderProvider(asset, provider);
    }

    void EnsureViewer(int size)
    {
        if (viewer is not null && viewer.Width == size)
        {
            return;
        }

        viewer?.Dispose();
        viewer = new SceneViewerService(vulkan, assets, (uint)size, (uint)size) { ClearColor = Vector4.Zero };
    }

    RasterImage? RenderProvider(Asset asset, IAssetPreviewProvider provider)
    {
        var service = viewer!;
        if (!SetOverlay(asset, provider))
        {
            return null;
        }

        AssetPreviewScene? scene = null;
        try
        {
            scene = BuildScene(asset, provider);
            service.Render(scene?.Root ?? emptyRoot, 0);
            var image = new RasterImage((int)service.Width, (int)service.Height);
            service.CopyPixels(image.Pixels);
            return image;
        }
        finally { scene?.OwnedResources?.Dispose(); }
    }

    bool SetOverlay(Asset asset, IAssetPreviewProvider provider)
    {
        viewer!.OverlayTexture = provider is ITexturePreviewProvider texture
            ? texture.GetPreviewTexture(asset, vulkan, assets) : null;
        return provider is not ITexturePreviewProvider || viewer.OverlayTexture is not null;
    }

    AssetPreviewScene? BuildScene(Asset asset, IAssetPreviewProvider provider)
    {
        if (provider is not IScenePreviewProvider sceneProvider)
        {
            return null;
        }

        var scene = sceneProvider.BuildPreview(asset, vulkan, assets);
        AssetPreviewServices.Initialize(scene, vulkan, assets);
        viewer!.FrameBounds(scene.Bounds);
        return scene;
    }

    /// <inheritdoc />
    public void Dispose() => viewer?.Dispose();
}
