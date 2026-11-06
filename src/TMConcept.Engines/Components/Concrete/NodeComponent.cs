using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Components;

/// <summary>
/// Node.js: the app's pinned copy, or a Node.js already on the PATH when it is at least <see cref="ToolManifest.NodeMinimumMajor"/> and has
/// npm next to it (then nothing is downloaded). The PATH is looked at once per run (<c>node --version</c>, a short process).
/// </summary>
public sealed class NodeComponent(ToolInstaller installer, IProcessRunner runner, ILogger<NodeComponent> logger)
    : ToolComponentBase(installer, ComponentCodes.Node, "Node.js", [ToolKind.Node], [])
{
    private bool _looked;

    /// <inheritdoc />
    public override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_looked || Installer.Status(ToolKind.Node) is { Ready: true, Source: "app" })
        {
            return;
        }

        _looked = true;
        if (Installer.FindProgram("node") is not { } node)
        {
            return;
        }

        var lines = new List<string>();
        try
        {
            var result = await runner.RunAsync(new ProcessStart(node, ["--version"], Path.GetDirectoryName(node)!), lines.Add, cancellationToken)
                .ConfigureAwait(false);
            var text = lines.FirstOrDefault(l => l.StartsWith('v'))?.Trim() ?? string.Empty;
            var major = text.Length > 1 && int.TryParse(text[1..].Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : 0;
            if (result.ExitCode != 0 || major < ToolManifest.NodeMinimumMajor)
            {
                logger.LogInformation("The system's Node.js ({Version}) is older than {Minimum}; the pinned Node.js is downloaded when needed",
                    text.Length > 0 ? text : "unknown", ToolManifest.NodeMinimumMajor);
                return;
            }

            if (EnginePackage.NpmCli(node) is null)
            {
                logger.LogInformation("The system's Node.js {Version} has no npm next to it; the pinned Node.js is downloaded when needed", text);
                return;
            }

            Installer.UseSystemNode(node);
            logger.LogInformation("Using the system's Node.js {Version} (no download)", text);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "The system's Node.js could not be checked; the pinned Node.js is downloaded when needed");
        }
    }
}
