namespace TMConcept.Desktop.Startup;

/// <summary>Raised when concepts change status, so every page that lists concepts reloads (one source of truth: the concept table).</summary>
public sealed class ConceptEvents
{
    /// <summary>Concepts changed; the sender is the page that changed them.</summary>
    public event EventHandler? Changed;

    /// <summary>Raises.</summary>
    public void Raise(object sender) => Changed?.Invoke(sender, EventArgs.Empty);
}
