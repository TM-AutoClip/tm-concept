namespace TMConcept.Core.Styles;

/// <summary>The ready closing sentences a template can pick (texts per language in <see cref="ClosingPhrases"/>; new values are added at the end).</summary>
public enum ClosingPhrase
{
    /// <summary>"Don't forget to subscribe to see what's next!"</summary>
    WhatsNext = 0,

    /// <summary>"Don't forget to subscribe for more videos!"</summary>
    MoreVideos = 1,

    /// <summary>"Don't forget to subscribe!"</summary>
    Short = 2,

    /// <summary>"Thanks for watching, don't forget to subscribe!"</summary>
    Thanks = 3,
}
