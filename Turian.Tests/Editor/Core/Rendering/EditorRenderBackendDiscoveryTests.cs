namespace Turian.Tests;

/// <summary>Backend discovery works in a fresh process with the published launcher's separate library folder.</summary>
public sealed class EditorRenderBackendDiscoveryTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("turian-render-discovery-").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>The backend is discovered beside Editor Core, with both flat and separate-library layouts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FindsBackendBesideEditorAssembly(bool separateLibraryFolder)
    {
        var lib = separateLibraryFolder ? Directory.CreateDirectory(Path.Combine(root, "lib")).FullName : root;
        Directory.CreateDirectory(Path.Combine(lib, "packages"));
        var assemblies = new[] { typeof(EditorRenderBackends).Assembly, typeof(VulkanEditorRenderBackend).Assembly };
        foreach (var assembly in assemblies)
        {
            File.Copy(assembly.Location, Path.Combine(lib, Path.GetFileName(assembly.Location)));
        }

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var token = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(ProbeSource, cancellationToken: token);
        var compilation = CSharpCompilation.Create("RenderDiscoveryProbe", [tree], references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        var probe = Path.Combine(root, "RenderDiscoveryProbe.dll");
        var result = compilation.Emit(probe, cancellationToken: token);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var runtimeConfig = Path.ChangeExtension(typeof(EditorRenderBackendDiscoveryTests).Assembly.Location,
            ".runtimeconfig.json");
        File.Copy(runtimeConfig, Path.ChangeExtension(probe, ".runtimeconfig.json"));

        var start = new ProcessStartInfo(DotnetLocator.Find())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { probe, lib, AppContext.BaseDirectory })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        Assert.True(process.ExitCode == 0,
            $"Backend discovery exited with {process.ExitCode}: {await output}{await error}");
        Assert.Equal("vulkan", (await output).Trim());
    }

    const string ProbeSource = """
        using System;
        using System.IO;
        using System.Runtime.Loader;

        var lib = args[0];
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(lib, name.Name + ".dll");
            if (!File.Exists(path))
            {
                path = Path.Combine(args[1], name.Name + ".dll");
            }

            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        var core = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(lib, "Turian.Editor.Core.dll"));
        var discovery = core.GetType("Turian.Editor.Core.EditorRenderBackends", throwOnError: true)!;
        var backend = discovery.GetMethod("Find")!.Invoke(null, new object[] { "vulkan" });
        if (backend is null)
        {
            throw new InvalidOperationException("No backend found beside Editor Core.");
        }

        Console.WriteLine(backend.GetType().GetProperty("Id")!.GetValue(backend));
        """;
}
