namespace TMConcept.Desktop.Dialogs;

/// <summary>What the user chose after a new app version started (<see cref="IUpdateDialog"/>).</summary>
public enum UpdateChoice
{
    /// <summary>Nothing now; asked again at the next start.</summary>
    Later = 0,

    /// <summary>Install again what was installed before and is now missing or changed.</summary>
    Update = 1,

    /// <summary>
    /// Delete the whole components folder for good first, then install again what was installed (28 Nov 2026); what is left unfinished is
    /// resumed from the install journal at the next start.
    /// </summary>
    Reinstall = 2,
}
