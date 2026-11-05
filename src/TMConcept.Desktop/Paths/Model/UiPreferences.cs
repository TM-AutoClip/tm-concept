namespace TMConcept.Desktop.Paths;

/// <summary>Plain preferences needed before unlocking (the language of the unlock window). Nothing secret.</summary>
public sealed class UiPreferences
{
    /// <summary>Interface language (<c>en</c> / <c>tr</c>); empty until chosen.</summary>
    public string Language { get; set; } = string.Empty;
}
