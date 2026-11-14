using System.CommandLine;
using Gaya.Host;
using Turian.Cli.Sdk;

namespace Turian.Cli.Plugins.Themes;

/// <summary>Adds <c>theme verify</c>, which checks studio theme sheets (.pss) with the Studio's own theme catalog.</summary>
public sealed class ThemeCliPlugin : ICliPlugin
{
    /// <inheritdoc />
    public IEnumerable<CliCommand> CreateCommands(CliPluginContext context)
    {
        var pathArg = new Argument<string>("path") { Description = "A .pss file, a folder of them, or a theme brick folder" };
        var verify = new Command("verify",
            "Parse and resolve every sheet, report unknown or mistyped tokens, and warn about low text contrast")
        {
            pathArg,
        };
        verify.SetAction(result => Verify(result.GetValue(pathArg)!));
        yield return new CliCommand(new Command("theme", "Check studio theme sheets (.pss) before installing them")
        {
            verify,
        });
    }

    static int Verify(string path)
    {
        IReadOnlyList<ThemeFinding> findings;
        try
        {
            findings = ThemeVerifier.Verify(path);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"{ex.FileName}: {ex.Message}");
            return 2;
        }

        foreach (var finding in findings)
        {
            Console.WriteLine(finding);
        }

        var errors = findings.Count(static f => f.Severity == ThemeFindingSeverity.Error);
        Console.WriteLine(errors == 0 ? "Theme sheets are valid" : $"{errors} error(s)");
        return errors == 0 ? 0 : 1;
    }
}
