namespace TMConcept.Desktop.Dialogs;

/// <summary>What the user chose after a new app version started (<see cref="IUpdateDialog"/>).</summary>
public enum UpdateChoice
{
    /// <summary>Nothing now; asked again at the next start.</summary>
    Later = 0,

    /// <summary>Install again what was installed before and is now missing or changed.</summary>
    Update = 1,

    /// <summary>Move the whole components folder to the recycle bin, then install what was installed before.</summary>
    Reinstall = 2,
}
