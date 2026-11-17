namespace TMConcept.Core.DiskSpace;

/// <summary>
/// Every component something needs, with who needs it (<see cref="IComponentUsage"/>): the saved step choices of all profiles and the app's
/// own needs, closed over the components' dependencies.
/// </summary>
public sealed record UsageMap(IReadOnlyDictionary<string, IReadOnlyList<DiskUse>> Uses)
{
    /// <summary>Nothing is used (no profile has a choice yet and the app needs nothing).</summary>
    public static UsageMap Empty { get; } = new(new Dictionary<string, IReadOnlyList<DiskUse>>(StringComparer.Ordinal));

    /// <summary>Whether <paramref name="code"/> is needed.</summary>
    public bool IsUsed(string code) => Uses.ContainsKey(code);

    /// <summary>Who needs <paramref name="code"/> (empty when nothing does).</summary>
    public IReadOnlyList<DiskUse> For(string code) => Uses.TryGetValue(code, out var uses) ? uses : [];
}
