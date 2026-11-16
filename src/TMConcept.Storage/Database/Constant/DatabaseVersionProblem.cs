namespace TMConcept.Storage.Database;

/// <summary>Why a database file was not opened or not upgraded (<see cref="DatabaseVersionException"/>).</summary>
public enum DatabaseVersionProblem
{
    /// <summary>The file has tables but no record of its schema steps: made by TM Concept 0.1.16 or earlier, before upgrades were recorded.</summary>
    Unrecorded,

    /// <summary>The file records a step this app does not know: a newer TM Concept made or upgraded it.</summary>
    Newer,

    /// <summary>A step failed; the file was left as it was before the upgrade.</summary>
    UpgradeFailed,
}
