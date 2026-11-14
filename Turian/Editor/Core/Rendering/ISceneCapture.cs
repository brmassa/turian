namespace Turian.Editor.Core;

/// <summary>What an offscreen capture renders besides the scene, and how large.</summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
public sealed record SceneCaptureOptions(uint Width, uint Height)
{
    /// <summary>Whether each render measures its CPU and GPU cost into <see cref="ISceneCapture.FrameStats"/>.</summary>
    public bool CollectStatistics { get; init; }

    /// <summary>Draws extra gizmos into every frame, or <c>null</c> for none.</summary>
    public Action<Gizmos>? PopulateGizmos { get; init; }

    /// <summary>Services the installed interface library is created from, or <c>null</c> for none.</summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>The input the interface reads during play, or <c>null</c> for a still capture.</summary>
    public IInputSource? Input { get; init; }

    /// <summary>Whether the interface behaves as in a running game rather than an edited scene.</summary>
    public bool Playing { get; init; }
}

/// <summary>
/// Renders scenes offscreen and reads the pixels back, for tools that capture without a window: screenshots,
/// headless play and thumbnails. A render backend supplies it through <see cref="IEditorRenderBackend"/>.
/// </summary>
public interface ISceneCapture : IDisposable
{
    /// <summary>Frame width in pixels.</summary>
    uint Width { get; }

    /// <summary>Frame height in pixels.</summary>
    uint Height { get; }

    /// <summary>The free camera used when <see cref="Render"/> is given no scene camera.</summary>
    EditorCamera Camera { get; }

    /// <summary>The last render's measurements, when <see cref="SceneCaptureOptions.CollectStatistics"/> is on.</summary>
    RenderFrameStats FrameStats { get; }

    /// <summary>The last render's frustum-culling counts.</summary>
    RenderCullingStats CullingStats { get; }

    /// <summary>The last completed frame's occlusion counts and memory, or <c>null</c> when occlusion culling is off.</summary>
    (RenderCullingStats Counts, ulong AllocatedBytes)? Occlusion { get; }

    /// <summary>Renders <paramref name="root"/>, with its interface, through <paramref name="camera"/> or <see cref="Camera"/>.</summary>
    /// <param name="root">The hierarchy to draw.</param>
    /// <param name="deltaTime">Seconds since the previous render, for animated interface content.</param>
    /// <param name="camera">A scene camera, or <c>null</c> for <see cref="Camera"/>.</param>
    void Render(Node root, double deltaTime, ICamera? camera = null);

    /// <summary>The last rendered frame.</summary>
    /// <returns>A new image of <see cref="Width"/> × <see cref="Height"/> pixels.</returns>
    RasterImage ReadPixels();
}

/// <summary>Turns an asset into a small preview image, honoring the preview providers its render backend offers.</summary>
public interface IAssetThumbnailRenderer : IDisposable
{
    /// <summary>A preview of <paramref name="entry"/> no larger than <paramref name="size"/>, or <c>null</c> without one.</summary>
    /// <param name="entry">The asset to preview.</param>
    /// <param name="size">The longest side in pixels.</param>
    /// <returns>The preview, or <c>null</c>.</returns>
    RasterImage? Render(AssetEntry entry, int size);
}
