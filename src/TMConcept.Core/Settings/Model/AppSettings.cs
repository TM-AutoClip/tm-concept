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

    /// <summary>Last automatic or manual backup (UTC); <c>null</c> before the first.</summary>
    public DateTimeOffset? LastBackupUtc { get; set; }

    /// <summary>The selected profile (channel) the pages are filtered by.</summary>
    public Guid? SelectedProfileId { get; set; }

    /// <summary>Accepted the Remotion license notice (Remotion is source-available, not open source).</summary>
    public bool RemotionLicenseAccepted { get; set; }

    /// <summary>Accepted the Supertonic OpenRAIL-M use restrictions.</summary>
    public bool SupertonicLicenseAccepted { get; set; }

    /// <summary>Accepted the non-commercial notice of the optional Piper Turkish voice (CC BY-NC-SA 4.0).</summary>
    public bool PiperNonCommercialAccepted { get; set; }
}
