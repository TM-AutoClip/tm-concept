using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// Starts the new version with <see cref="UpdateRules.AfterUpdateArgument"/> and this process's id (it waits until this one has ended and
/// released the data folder) and the same data folder option, then closes the app the normal way (its children end with it).
/// </summary>
public sealed class AppRestarter(ILogger<AppRestarter> logger) : IAppRestarter
{
    /// <inheritdoc />
    public void Restart(string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        start.ArgumentList.Add(UpdateRules.AfterUpdateArgument);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        if (StartOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToList()).DataDirectory is { } data)
        {
            start.ArgumentList.Add("--data-dir");
            start.ArgumentList.Add(data);
        }

        using (Process.Start(start))
        {
            logger.LogInformation("The new version was started; this one closes");
        }

        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
