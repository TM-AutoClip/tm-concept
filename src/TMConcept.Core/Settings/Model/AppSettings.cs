namespace TMConcept.Core.Settings;

/// <summary>Settings kept in the encrypted database (one JSON document).</summary>
public sealed class AppSettings
{
    /// <summary>Interface language (<c>en</c> / <c>tr</c>).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Bottom-right notifications on.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>How long a notification stays (seconds; default one minute).</summary>
    public int NotificationSeconds { get; set; } = 60;

    /// <summary>The selected profile (channel) the pages are filtered by.</summary>
    public Guid? SelectedProfileId { get; set; }

    /// <summary>Accepted the Remotion license notice (Remotion is source-available, not open source).</summary>
    public bool RemotionLicenseAccepted { get; set; }

    /// <summary>Accepted the Supertonic OpenRAIL-M use restrictions.</summary>
    public bool SupertonicLicenseAccepted { get; set; }

    /// <summary>
    /// Model licences the user accepted (<c>provider/id@licence</c>; a model that changes its licence asks again): the non-commercial voices
    /// (XTTS-v2, F5-TTS Turkish, MMS) are offered with their licence and used only after it was accepted.
    /// </summary>
    public List<string> AcceptedModelLicenses { get; set; } = [];

    /// <summary>
    /// Answer limit (max_tokens, thinking included) of "Suggest topics" (default 16 000; Settings → General; kept within the topic limits and
    /// lowered to the model's own limit). Only what the model writes is paid for.
    /// </summary>
    public int TopicMaxOutputTokens { get; set; } = 16_000;
}
