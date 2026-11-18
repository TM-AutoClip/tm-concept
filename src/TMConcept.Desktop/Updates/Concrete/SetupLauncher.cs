using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// Starts Setup directly (no shell, no command interpreter, no <c>--installto</c>: it installs per user into <c>%LOCALAPPDATA%\TMConcept</c>,
/// apart from the data folder). A file the app downloaded itself has no "downloaded from the internet" mark, so Windows shows no SmartScreen
/// question for it.
/// </summary>
public sealed class SetupLauncher(ILogger<SetupLauncher> logger) : ISetupLauncher
{
    /// <inheritdoc />
    public void Start(string setup)
    {
        ArgumentException.ThrowIfNullOrEmpty(setup);
        using (Process.Start(new ProcessStartInfo(setup) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(setup)! }))
        {
            logger.LogInformation("Setup was started ({File})", Path.GetFileName(setup));
        }
    }
}
