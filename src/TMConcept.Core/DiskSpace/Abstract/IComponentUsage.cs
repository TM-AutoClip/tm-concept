namespace TMConcept.Core.DiskSpace;

/// <summary>
/// Which components are needed and by whom: every profile's saved step choices (the catalog model's components, the video code step's
/// engine), the app's own needs (ffmpeg), and everything those depend on. A component outside it is unused and may be cleaned up.
/// </summary>
public interface IComponentUsage
{
    /// <summary>What is needed now (reads the profiles and their saved choices).</summary>
    Task<UsageMap> ComputeAsync(CancellationToken cancellationToken);

    /// <summary>The components one saved choice needs directly (before dependencies).</summary>
    IReadOnlyList<string> CodesOf(Models.StepModelChoice choice);
}
