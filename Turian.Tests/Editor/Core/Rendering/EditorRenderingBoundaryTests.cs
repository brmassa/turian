using System.CommandLine;
using Turian.Editor.CLI;

namespace Turian.Tests;

/// <summary>
/// Editor logic and the CLI stay usable headlessly and with any renderer or GUI toolkit: they reach graphics only
/// through <see cref="IEditorRenderBackend"/> and never link a GUI library.
/// </summary>
public sealed class EditorRenderingBoundaryTests
{
    static bool IsRendererOrGui(string name) =>
        name is "Turian.Engine.Vulkan" or "Turian.Editor.Rendering.Vulkan" or "SkiaSharp" or "Gaya.Host" or "Gaya.Sdk"
        || name.StartsWith("Guinevere", StringComparison.Ordinal)
        || name.StartsWith("Silk.NET.Vulkan", StringComparison.Ordinal)
        || name.StartsWith("Silk.NET.GLFW", StringComparison.Ordinal)
        || (name.StartsWith("Silk.NET.Windowing", StringComparison.Ordinal) && name != "Silk.NET.Windowing.Common");

    /// <summary>Editor Core reaches no render backend, window backend or GUI toolkit, directly or transitively.</summary>
    [Fact]
    public void EditorCoreReachesNoRendererOrGuiToolkit()
    {
        var offenders = AssemblyClosure.Offenders(typeof(AssetImporter).Assembly, IsRendererOrGui);

        Assert.True(offenders.Length == 0, $"Editor.Core reaches: {string.Join("; ", offenders)}.");
    }

    /// <summary>The CLI ships the default backend but its code names none, so another backend replaces it unchanged.</summary>
    [Fact]
    public void CliReachesNoRendererOrGuiToolkit()
    {
        var offenders = AssemblyClosure.Offenders(typeof(Program).Assembly, IsRendererOrGui);

        Assert.True(offenders.Length == 0, $"The CLI reaches: {string.Join("; ", offenders)}.");
    }

    /// <summary>The installed backend is found by id, case-insensitively, and an unknown id finds none.</summary>
    [Fact]
    public void BackendsAreFoundById()
    {
        Assert.IsType<VulkanEditorRenderBackend>(EditorRenderBackends.Find());
        Assert.IsType<VulkanEditorRenderBackend>(EditorRenderBackends.Find(" VULKAN "));
        Assert.Null(EditorRenderBackends.Find("metal"));
    }

    /// <summary>The backend registers its device under the neutral contract and its own type, as one instance.</summary>
    [Fact]
    public void BackendRegistersOneDeviceUnderBothTypes()
    {
        var services = new ServiceCollection();
        new VulkanEditorRenderBackend().ConfigureServices(services);

        Assert.Contains(services, static s => s.ServiceType == typeof(Vulkan));
        Assert.Contains(services, static s => s.ServiceType == typeof(IRenderDevice));
        Assert.Throws<ArgumentException>(static () => new VulkanEditorRenderBackend()
            .CreateCapture(Substitute.For<IRenderDevice>(), new AssetDatabase(), new SceneCaptureOptions(1, 1)));
    }

    /// <summary>Optional commands installed beside the CLI register through the plugin contract.</summary>
    [Fact]
    public void BuiltInPluginsBesideTheCliRegisterTheirCommands()
    {
        var root = new RootCommand();
        CliPlugins.LoadBuiltIn(new CliPlugins.CommandRegistry(root), AppContext.BaseDirectory, null, NullLogger.Instance);

        var theme = Assert.Single(root.Subcommands, static command => command.Name == "theme");
        Assert.Contains(theme.Subcommands, static command => command.Name == "verify");
    }
}
