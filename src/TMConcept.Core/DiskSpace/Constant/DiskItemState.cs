namespace TMConcept.Core.DiskSpace;

/// <summary>What a storage row is now.</summary>
public enum DiskItemState
{
    /// <summary>A component that a profile's saved step choice (or the app itself) needs.</summary>
    Used,

    /// <summary>A ready component nothing needs now (a voice you switched away from, for example).</summary>
    Unused,

    /// <summary>A component on disk that is not usable (a half-finished download, or a part it needs is missing).</summary>
    Incomplete,

    /// <summary>Space no component owns: leftovers, caches and old folders that can be made again (or are not needed).</summary>
    Leftover,
}
