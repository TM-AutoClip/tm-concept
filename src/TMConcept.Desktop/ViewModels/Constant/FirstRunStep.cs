namespace TMConcept.Desktop.ViewModels;

/// <summary>Page of the first-run wizard.</summary>
public enum FirstRunStep
{
    /// <summary>Choose the language (two buttons).</summary>
    Language = 0,

    /// <summary>New start or restore (shown only while there is no data).</summary>
    Choice = 1,

    /// <summary>New start: protection and backup password.</summary>
    New = 2,

    /// <summary>Restore from a backup file.</summary>
    Restore = 3,
}
