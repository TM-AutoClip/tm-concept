namespace TMConcept.Storage.Keys;

/// <summary>Gives the master password when the database is protected with one (the desktop app asks the user).</summary>
public interface IMasterPasswordSource
{
    /// <summary>The password; <paramref name="confirm"/> asks twice (when a new one is set).</summary>
    Task<string> GetAsync(bool confirm, CancellationToken cancellationToken);
}
