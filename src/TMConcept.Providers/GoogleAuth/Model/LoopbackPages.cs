namespace TMConcept.Providers.GoogleAuth;

/// <summary>The two short pages the browser shows after the sign-in comes back to the app (plain text, in the app's language).</summary>
public sealed record LoopbackPages(string Done, string Failed)
{
    /// <summary>English pages.</summary>
    public static LoopbackPages English { get; } = new("TM Concept: signed in. You can close this tab and go back to the app.",
        "TM Concept: the sign-in did not finish. Go back to the app and try again.");
}
