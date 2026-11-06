using System.Globalization;
using System.Text;
using TMConcept.Core.Engines;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Reports;

/// <summary>
/// <c>system.txt</c> of a problem report (also what "Copy details" copies): the program, its version and commit, the operating system,
/// architecture and .NET, the interface language, how the window is drawn, whether the data folder is portable, and which tools and engines
/// are installed. No setting values, no keys, no paths, no names of profiles or accounts, nothing from the database.
/// </summary>
public sealed class SystemReport(Localizer text, UiPreferencesFile preferences, IToolLocator tools, IEnumerable<IVideoEngine> engines, AppPaths paths,
    TimeProvider clock)
{
    /// <summary>The text.</summary>
    public string Build()
    {
        var c = CultureInfo.InvariantCulture;
        var report = new StringBuilder();
        report.AppendLine(c, $"{AppIdentity.Name} problem report");
        report.AppendLine(c, $"Created: {clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm zzz", c)}");
        report.AppendLine(c, $"App: {AppIdentity.Name} {AppIdentity.Version}");
        report.AppendLine(c, $"OS: {AppIdentity.Os}");
        report.AppendLine(c, $"Architecture: {AppIdentity.Architecture}");
        report.AppendLine(c, $".NET: {AppIdentity.Runtime}");
        report.AppendLine(c, $"Interface language: {text.Language}");
        report.AppendLine(c, $"Drawing: {(preferences.Read().UseGraphicsCard ? "graphics card" : "processor")} (as set; takes effect at start)");
        report.AppendLine(c, $"Data folder: {(paths.IsPortable ? "portable (--data-dir)" : "default")}");
        report.AppendLine("Tools:");
        foreach (var tool in Enum.GetValues<ToolKind>().Where(t => t != ToolKind.ImageWeights))
        {
            var status = tools.Status(tool);
            var state = !status.Ready ? "not installed"
                : status.Source == Engines.Tools.ToolInstaller.FallbackSource ? "the system's copy"
                : "installed" + (status.Version.Length > 0 ? " (" + status.Version + ")" : string.Empty);
            report.AppendLine(c, $"  {tool}: {state}");
        }

        report.AppendLine("Engines:");
        foreach (var engine in engines.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var status = engine.Status();
            report.AppendLine(c, $"  {engine.Name}: {(status.Ready ? "ready" : "not ready (missing: " + string.Join(", ", status.Missing) + ")")}");
        }

        return report.ToString();
    }
}
