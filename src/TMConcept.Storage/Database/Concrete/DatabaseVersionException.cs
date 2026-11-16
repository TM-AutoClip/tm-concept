namespace TMConcept.Storage.Database;

/// <summary>
/// The database file has a schema this app cannot use as it is (<see cref="Problem"/>). The file itself is never changed by the check; a
/// failed upgrade leaves it exactly as it was. An <see cref="InvalidOperationException"/>, so the restore check refuses such a backup.
/// </summary>
public sealed class DatabaseVersionException : InvalidOperationException
{
    /// <summary>Creates.</summary>
    public DatabaseVersionException()
    {
    }

    /// <summary>Creates.</summary>
    public DatabaseVersionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public DatabaseVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>What is wrong.</summary>
    public DatabaseVersionProblem Problem { get; init; }

    /// <summary>The step concerned: the unknown newer one, or the one that failed (its name, <see cref="SchemaUpgrader.Name"/>); empty otherwise.</summary>
    public string Step { get; init; } = string.Empty;
}
