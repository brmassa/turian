namespace Turian.Editor.Core;

/// <summary>
/// What the editor and CLI need from a render backend: a device, offscreen captures and asset thumbnails. Tools reach
/// a renderer only through this, so one backend replaces another without changing them.
/// </summary>
public interface IEditorRenderBackend
{
    /// <summary>A short id, such as <c>vulkan</c>, that <see cref="EditorRenderBackends.Find"/> selects by.</summary>
    string Id { get; }

    /// <summary>Bytes of texture data the backend currently holds on its devices, for headless statistics.</summary>
    ulong UploadedTextureBytes { get; }

    /// <summary>Disposes the models cached on the backend's devices, so the next scene reloads them.</summary>
    void ReleaseCachedModels();

    /// <summary>
    /// Registers the backend's device as <see cref="IRenderDevice"/> and under its own type, created on first use
    /// from the container's <see cref="ILogger"/> and <see cref="IAppSettings"/>.
    /// </summary>
    /// <param name="services">The collection to add to.</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>Creates a headless device that ignores project graphics settings.</summary>
    /// <param name="logger">The logger the device reports through.</param>
    /// <returns>The device; the caller disposes it when it is disposable.</returns>
    IRenderDevice CreateDevice(ILogger logger);

    /// <summary>Creates an offscreen capture on a device this backend created.</summary>
    /// <param name="device">The device to render with.</param>
    /// <param name="assets">The asset database models, materials and textures are read from.</param>
    /// <param name="options">Size and what to draw besides the scene.</param>
    /// <returns>The capture; the caller disposes it.</returns>
    ISceneCapture CreateCapture(IRenderDevice device, AssetDatabase assets, SceneCaptureOptions options);

    /// <summary>Creates the thumbnail renderer the asset browser draws previews with.</summary>
    /// <param name="device">The device to render with.</param>
    /// <param name="assets">The asset database previews read from.</param>
    /// <param name="catalog">The preview providers the editor discovered.</param>
    /// <returns>The renderer; the caller disposes it.</returns>
    IAssetThumbnailRenderer CreateThumbnailRenderer(IRenderDevice device, AssetDatabase assets, AssetPreviewCatalog catalog);
}

/// <summary>Identifies the <see cref="IEditorRenderBackend"/> an assembly provides.</summary>
/// <param name="backendType">A type with a public parameterless constructor implementing the backend.</param>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EditorRenderBackendAttribute(Type backendType) : Attribute
{
    /// <summary>The backend type.</summary>
    public Type BackendType { get; } = backendType;
}

/// <summary>
/// Finds the installed render backend: loaded assemblies first, then <c>Turian.Editor.Rendering.*</c> assemblies
/// beside Editor Core and in the built-in bricks' editor folders.
/// </summary>
public static class EditorRenderBackends
{
    /// <summary>The file-name prefix render backend editor assemblies use, so discovery loads nothing else.</summary>
    public const string AssemblyPrefix = "Turian.Editor.Rendering.";

    /// <summary>
    /// The backend whose <see cref="IEditorRenderBackend.Id"/> is <paramref name="id"/>, or the first installed one
    /// when <paramref name="id"/> is empty; <c>null</c> when none matches, so a tool can run without graphics.
    /// </summary>
    /// <param name="id">A backend id, or <c>null</c>.</param>
    /// <returns>The backend, or <c>null</c>.</returns>
    public static IEditorRenderBackend? Find(string? id = null)
    {
        LoadCandidates();
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .Select(static assembly => assembly.GetCustomAttribute<EditorRenderBackendAttribute>())
            .OfType<EditorRenderBackendAttribute>()
            .OrderBy(static attribute => attribute.BackendType.FullName, StringComparer.Ordinal)
            .Select(static attribute => (IEditorRenderBackend)Activator.CreateInstance(attribute.BackendType)!)
            .FirstOrDefault(backend => string.IsNullOrWhiteSpace(id)
                                       || string.Equals(backend.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    static void LoadCandidates()
    {
        var editorDirectory = Path.GetDirectoryName(typeof(EditorRenderBackends).Assembly.Location)
                              ?? AppContext.BaseDirectory;
        var directories = new List<string> { editorDirectory };
        if (Directory.Exists(ProjectPackages.BuiltinDirectory))
        {
            directories.AddRange(Directory.EnumerateDirectories(ProjectPackages.BuiltinDirectory)
                .Select(static brick => Path.Combine(brick, "Precast~", "editor"))
                .Where(Directory.Exists));
        }

        foreach (var path in directories.SelectMany(static directory =>
                     Directory.EnumerateFiles(directory, $"{AssemblyPrefix}*.dll")))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(loaded => loaded.GetName().Name == name))
            {
                Load(name, path);
            }
        }
    }

    // A backend the tool was published with resolves from its dependency list; a brick's copy loads by path.
    static void Load(string name, string path)
    {
        try
        {
            ReflectionAssembly.Load(new AssemblyName(name));
        }
        catch (FileNotFoundException)
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
    }
}
