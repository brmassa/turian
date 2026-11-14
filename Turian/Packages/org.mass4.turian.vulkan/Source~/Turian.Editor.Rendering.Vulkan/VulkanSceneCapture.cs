namespace Turian.Editor.Core;

/// <summary>
/// An offscreen <see cref="SceneViewerService"/> with the installed interface library composited over it. The overlay
/// and world-panel sources pull from the last rendered root, and disposal releases the viewer before the interface
/// it feeds.
/// </summary>
public sealed class VulkanSceneCapture : ISceneCapture
{
    readonly SceneViewerService viewer;
    readonly IUiPresenter? presenter;
    Node? overlayRoot;

    /// <summary>Creates a capture on <paramref name="vulkan"/>.</summary>
    /// <param name="vulkan">The device to render with.</param>
    /// <param name="assets">The asset database content is read from.</param>
    /// <param name="options">Size and what to draw besides the scene.</param>
    /// <param name="interfaces">The interface library, or <c>null</c> to discover the installed one.</param>
    public VulkanSceneCapture(Vulkan vulkan, AssetDatabase assets, SceneCaptureOptions options,
        IUiPresenterFactory? interfaces = null)
    {
        ArgumentNullException.ThrowIfNull(vulkan);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(options);

        viewer = new SceneViewerService(vulkan, assets, options.Width, options.Height)
        {
            CollectStatistics = options.CollectStatistics,
            OnPopulateGizmos = options.PopulateGizmos,
        };
        presenter = (interfaces ?? UiPresenters.Find())?.Create(vulkan, options.Input, options.Services);
        if (presenter is null)
        {
            return;
        }

        presenter.IsPlaying = options.Playing;
        viewer.OverlaySource = (w, h, dt) =>
            overlayRoot is null ? null : presenter.TryRenderOverlay(overlayRoot, (int)w, (int)h, dt);
        viewer.WorldUiSource = frame =>
            overlayRoot is null ? [] : presenter.RenderWorldPanels(overlayRoot, frame);
    }

    /// <inheritdoc />
    public uint Width => viewer.Width;

    /// <inheritdoc />
    public uint Height => viewer.Height;

    /// <inheritdoc />
    public EditorCamera Camera => viewer.Camera;

    /// <inheritdoc />
    public RenderFrameStats FrameStats => viewer.FrameStats;

    /// <inheritdoc />
    public RenderCullingStats CullingStats => viewer.CullingStats;

    /// <inheritdoc />
    public (RenderCullingStats Counts, ulong AllocatedBytes)? Occlusion =>
        viewer.UseOcclusionCulling ? (viewer.OcclusionStats, viewer.OcclusionAllocatedBytes) : null;

    /// <inheritdoc />
    public void Render(Node root, double deltaTime, ICamera? camera = null)
    {
        overlayRoot = root;
        viewer.Render(root, deltaTime, camera);
    }

    /// <inheritdoc />
    public RasterImage ReadPixels()
    {
        var image = new RasterImage((int)viewer.Width, (int)viewer.Height);
        viewer.CopyPixels(image.Pixels);
        return image;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        viewer.OverlaySource = null;
        viewer.WorldUiSource = null;
        overlayRoot = null;
        viewer.Dispose();
        presenter?.Dispose();
    }
}
