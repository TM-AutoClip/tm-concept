namespace TMConcept.Desktop.DiskSpace;

/// <summary>When the start asks about unused model files (0.1.54, <see cref="IOrphanFileCheck"/>).</summary>
public static class OrphanFileRules
{
    /// <summary>The least the unused files must take before the start asks (500 MB).</summary>
    public const long DefaultAskFromBytes = 500L * 1024 * 1024;

    /// <summary>How long a start does not ask again after it asked.</summary>
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(1);

    /// <summary>The most rows the window lists (the rest are counted).</summary>
    public const int ListMax = 15;
}
