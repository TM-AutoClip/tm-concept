namespace TMConcept.Desktop.Updates;

/// <summary>
/// Where and how often the app looks for a new version of itself: the latest release of the project on GitHub, read without a token
/// (GitHub allows 60 such reads an hour per address), at most once a day, with a plain program name as its User-Agent (nothing about the
/// user or the computer is sent).
/// </summary>
public static class UpdateRules
{
    /// <summary>The latest published release (drafts and pre-releases are not "latest").</summary>
    public static readonly Uri LatestRelease = new("https://api.github.com/repos/TM-AutoClip/tm-concept/releases/latest");

    /// <summary>The releases page (shown when the app cannot install a release itself).</summary>
    public static readonly Uri ReleasesPage = new("https://github.com/TM-AutoClip/tm-concept/releases");

    /// <summary>The User-Agent GitHub requires.</summary>
    public const string UserAgent = "TM-Concept-Updater";

    /// <summary>The API version header value.</summary>
    public const string ApiVersion = "2022-11-28";

    /// <summary>How often the start looks (the "Check now" button always looks).</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

    /// <summary>The longest wait for GitHub's answer.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The argument the restarted app gets: wait until the old process (its id follows) has ended before starting.</summary>
    public const string AfterUpdateArgument = "--after-update";

    /// <summary>The name the replaced program file gets next to the new one (Windows; removed at the next start).</summary>
    public const string OldSuffix = ".old";

    /// <summary>The folder next to the program where a new version is downloaded and unpacked before it replaces the program.</summary>
    public const string StagingFolder = ".tm-concept-update";
}
