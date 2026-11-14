namespace Turian.Editor.Core;

/// <summary>Identifies a thumbnail by asset, import revision, preview provider and resolution.</summary>
public readonly record struct AssetThumbnailKey(Guid AssetId, string Path, string Revision, int Size);

/// <summary>
/// Owns a bounded cache and renders a limited number of queued thumbnails on the render thread. The image type is
/// whatever the interface library draws, such as a GPU-backed image; disposable images are released on eviction.
/// </summary>
/// <typeparam name="TImage">The image type the interface library draws.</typeparam>
public sealed class AssetThumbnailCache<TImage>(Func<AssetEntry, int, TImage?> render, int capacity = 512) : IDisposable
    where TImage : class
{
    sealed record Request(AssetThumbnailKey Key, AssetEntry Entry);
    sealed record Cached(TImage? Image, LinkedListNode<AssetThumbnailKey> Node);

    readonly Dictionary<AssetThumbnailKey, Cached> images = [];
    readonly LinkedList<AssetThumbnailKey> recent = [];
    readonly Queue<Request> pending = [];
    readonly HashSet<AssetThumbnailKey> queued = [];
    readonly List<TImage> retired = [];

    /// <summary>Releases evicted images after the preceding frame has finished drawing them.</summary>
    public void BeginFrame()
    {
        foreach (var image in retired)
        {
            (image as IDisposable)?.Dispose();
        }

        retired.Clear();
    }

    /// <summary>The number of retained thumbnail results, including unsupported previews.</summary>
    public int Count => images.Count;

    /// <summary>The number of thumbnail requests awaiting production on the render thread.</summary>
    public int PendingCount => pending.Count;

    /// <summary>Returns a cached image or schedules its production; requests are deduplicated.</summary>
    public TImage? RequestImage(AssetEntry entry, string revision, int size = 128)
    {
        var bucket = size <= 64 ? 64 : size <= 128 ? 128 : 256;
        var key = new AssetThumbnailKey(entry.AssetMetadata?.Id ?? Guid.Empty, entry.AbsolutePath, revision, bucket);
        if (images.TryGetValue(key, out var cached))
        {
            recent.Remove(cached.Node);
            recent.AddLast(cached.Node);
            return cached.Image;
        }
        if (queued.Add(key))
        {
            pending.Enqueue(new Request(key, entry));
        }

        return null;
    }

    /// <summary>Produces at most the supplied budget of thumbnails; failed previews retain their fallback icons.</summary>
    public void Process(int budget = 2)
    {
        for (var i = 0; i < budget && pending.TryDequeue(out var request); i++)
        {
            Produce(request);
        }
    }

    void Produce(Request request)
    {
        queued.Remove(request.Key);
        TImage? image = null;
        try { image = render(request.Entry, request.Key.Size); }
        catch (Exception ex) { Log.Logger.LogDebug(ex, "Could not preview {Path}", request.Entry.AbsolutePath); }
        images.Add(request.Key, new Cached(image, recent.AddLast(request.Key)));
        Trim();
    }

    void Trim()
    {
        while (images.Count > Math.Max(1, capacity))
        {
            var key = recent.First!.Value;
            if (images[key].Image is { } image)
            {
                retired.Add(image);
            }

            images.Remove(key);
            recent.RemoveFirst();
        }
    }

    /// <summary>Disposes all images and cancels pending work when imports or project contents change.</summary>
    public void Clear()
    {
        BeginFrame();
        foreach (var cached in images.Values)
        {
            (cached.Image as IDisposable)?.Dispose();
        }

        images.Clear();
        recent.Clear();
        pending.Clear();
        queued.Clear();
    }

    /// <inheritdoc />
    public void Dispose() => Clear();
}
