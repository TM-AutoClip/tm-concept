namespace TMConcept.Desktop.Paths;

/// <summary>Command line options.</summary>
public sealed record StartOptions(string? DataDirectory)
{
    /// <summary>Parses <c>--data-dir &lt;folder&gt;</c>; the environment variable TMCONCEPT_DATA_DIR is the fallback.</summary>
    public static StartOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? data = null;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--data-dir")
            {
                data = args[i + 1];
            }
        }

        data ??= Environment.GetEnvironmentVariable("TMCONCEPT_DATA_DIR");
        return new StartOptions(string.IsNullOrWhiteSpace(data) ? null : Path.GetFullPath(data));
    }
}
