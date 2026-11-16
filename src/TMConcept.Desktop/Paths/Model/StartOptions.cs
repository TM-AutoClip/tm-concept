namespace TMConcept.Desktop.Paths;

/// <summary>
/// Command line options: the data folder, and (after an update, <see cref="Updates.UpdateRules.AfterUpdateArgument"/>) the id of the old
/// process to wait for before this one takes the data folder.
/// </summary>
public sealed record StartOptions(string? DataDirectory, int? WaitForProcess = null)
{
    /// <summary>
    /// Parses <c>--data-dir &lt;folder&gt;</c> (the environment variable TMCONCEPT_DATA_DIR is the fallback) and
    /// <c>--after-update &lt;process id&gt;</c>.
    /// </summary>
    public static StartOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? data = null;
        int? wait = null;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--data-dir")
            {
                data = args[i + 1];
            }
            else if (args[i] == Updates.UpdateRules.AfterUpdateArgument
                && int.TryParse(args[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
            {
                wait = id;
            }
        }

        data ??= Environment.GetEnvironmentVariable("TMCONCEPT_DATA_DIR");
        return new StartOptions(string.IsNullOrWhiteSpace(data) ? null : Path.GetFullPath(data), wait);
    }
}
