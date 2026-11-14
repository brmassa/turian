using MASS4.Bricks;

namespace Turian.Editor.Core;

/// <summary>The project's brick manifest, available before asset loading and edited with other project settings.</summary>
public sealed class BricksSettings : ProjectManifest
{
    static readonly ConditionalWeakTable<IAppSettings, StrongBox<BricksSettings?>> ByProject = [];
    readonly string projectFolder;

    /// <summary>Reads the project's declared dependencies without applying machine-specific overrides.</summary>
    public BricksSettings(string projectFolder)
    {
        this.projectFolder = projectFolder;
        Reload();
    }

    /// <summary>
    /// The brick settings of <paramref name="settings"/>'s project, shared by every caller until its project folder
    /// changes. The runtime never reads them, so they belong to the tools rather than to the engine's settings.
    /// </summary>
    public static BricksSettings Of(IAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var current = ByProject.GetOrCreateValue(settings);
        return current.Value is { } loaded && loaded.projectFolder == settings.ProjectAbsoluteDir
            ? loaded
            : current.Value = new BricksSettings(settings.ProjectAbsoluteDir);
    }

    /// <summary>Reads the manifest again after a package operation or an external edit.</summary>
    public void Reload()
    {
        var manifest = string.IsNullOrWhiteSpace(projectFolder)
            ? new ProjectManifest()
            : Load(projectFolder, includeUserOverride: false).Manifest;
        Dependencies = manifest.Dependencies;
        ScopedRegistries = manifest.ScopedRegistries;
    }

    /// <summary>Writes the declared dependencies and registries to the project's shared manifest.</summary>
    public void Save()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        Save(projectFolder);
    }
}

/// <summary>Gives any open project its brick settings without the runtime depending on the brick system.</summary>
public static class BricksSettingsExtensions
{
    extension(IAppSettings settings)
    {
        /// <summary>The project's declared brick dependencies and registries, read independently of game assets.</summary>
        public BricksSettings Bricks => BricksSettings.Of(settings);
    }
}
