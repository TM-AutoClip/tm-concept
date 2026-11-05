namespace TMConcept.Storage.Keys;

/// <summary>State of the encrypted database.</summary>
public enum VaultState
{
    /// <summary>First start: no key file yet (the wizard offers a new start or a restore).</summary>
    NotCreated = 0,

    /// <summary>The key file exists; the database is not open yet.</summary>
    Locked = 1,

    /// <summary>Open.</summary>
    Open = 2,
}
