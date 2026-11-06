namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// The key gate's dialog: a process needs an online provider that has no API key yet. The user can paste one right there; it is tested with
/// the provider's free request and saved only when the test passes. Closed without a working key = the process does not start.
/// </summary>
public interface IApiKeyDialog
{
    /// <summary>Asks for a key of <paramref name="provider"/>; true when a working key was saved.</summary>
    Task<bool> AddKeyAsync(string provider);

    /// <summary>The display name of a provider code (the code itself when the app does not know it).</summary>
    string ProviderName(string provider);
}
