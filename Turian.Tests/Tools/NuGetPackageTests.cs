namespace Turian.Tests;

/// <summary>Checks the NuGet distribution boundary for the shared Bricks library.</summary>
public sealed class NuGetPackageTests
{
    /// <summary>Only the shared Bricks library is published from this solution.</summary>
    [Fact]
    public void NuGetPublication_ContainsOnlySharedBricksLibrary()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Turian.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var solution = XDocument.Load(Path.Combine(root.FullName, "Turian.slnx"));
        var packageIds = solution.Descendants("Project")
            .Select(project => Path.Combine(root.FullName, project.Attribute("Path")!.Value))
            .SelectMany(static path => XDocument.Load(path).Descendants("PackageId")
                .Select(static package => package.Value))
            .ToArray();

        Assert.Equal("MASS4.Gaya.Packages", Assert.Single(packageIds));
    }
}
