namespace TMConcept.Desktop.Startup;

/// <summary>What a start does about the app version that ran before (<see cref="VersionCheck.Decide"/>).</summary>
public enum VersionChange
{
    /// <summary>The same version ran before: nothing.</summary>
    Same = 0,

    /// <summary>Another version (or none recorded) and no component installed: the version is only recorded.</summary>
    Record = 1,

    /// <summary>Another version (or none recorded: 0.1.15 and earlier did not record it) with components installed: the user is asked.</summary>
    Ask = 2,
}
