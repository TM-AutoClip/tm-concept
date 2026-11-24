namespace TMConcept.Desktop.ViewModels;

/// <summary>One step of a help page: its number, its text in the current language and the page it is done on (null = in this app).</summary>
public sealed record HelpStep(int Number, string Text, Uri? Link)
{
    /// <summary>Whether the step has a page to open.</summary>
    public bool HasLink => Link is not null;
}
