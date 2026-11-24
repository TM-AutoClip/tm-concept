namespace TMConcept.Desktop.Startup;

/// <summary>
/// Raised when concepts change status, so every page that lists concepts reloads (one source of truth: the concept table); and when
/// settings may have changed (0.1.40), so a page that keeps what it read from them (the concepts page's model bar) reads them again.
/// </summary>
public sealed class ConceptEvents
{
    /// <summary>Concepts changed; the sender is the page that changed them.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Settings may have changed (keys, servers, models, a profile, the language): the user left a page that changes them. The sender is
    /// that page.
    /// </summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Raises <see cref="Changed"/>.</summary>
    public void Raise(object sender) => Changed?.Invoke(sender, EventArgs.Empty);

    /// <summary>Raises <see cref="SettingsChanged"/>.</summary>
    public void RaiseSettings(object sender) => SettingsChanged?.Invoke(sender, EventArgs.Empty);
}
