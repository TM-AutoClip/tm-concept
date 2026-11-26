namespace TMConcept.Desktop.Updates;

/// <summary>
/// Where and how often the app looks for a new version of itself: the project's release list on GitHub, read without a token
/// (GitHub allows 60 such reads an hour per address), at every start and on "Check now", as a conditional request (an unchanged list is a
/// 304 that does not count), with a plain program name as its User-Agent (nothing about the user or the computer is sent).
/// </summary>
public static class UpdateRules
{
    /// <summary>
    /// The project's releases, newest first, 100 per page (the update window lists the notes of every version between the running one and
    /// the newest; drafts and pre-releases are left out by the app).
    /// </summary>
    public static readonly Uri Releases = new("https://api.github.com/repos/TM-AutoClip/tm-concept/releases?per_page=100");

    /// <summary>Where a "next page" address of the release list must start (GitHub's Link header; anything else is not followed).</summary>
    public const string ApiPrefix = "https://api.github.com/";

    /// <summary>The most pages of the release list read in one look (each one counts against the hourly limit).</summary>
    public const int MaxPages = 5;

    /// <summary>The project's repository (Velopack's GitHub source for the installed edition reads its releases).</summary>
    public const string Repository = "https://github.com/TM-AutoClip/tm-concept";

    /// <summary>The releases page (shown when the app cannot install a release itself).</summary>
    public static readonly Uri ReleasesPage = new("https://github.com/TM-AutoClip/tm-concept/releases");

    /// <summary>The User-Agent GitHub requires.</summary>
    public const string UserAgent = "TM-Concept-Updater";

    /// <summary>The API version header value.</summary>
    public const string ApiVersion = "2022-11-28";

    /// <summary>
    /// How long the start does not offer again a version the user answered "Later" to (0.1.54: the start always looks; only the window waits).
    /// "Check now" always shows it.
    /// </summary>
    public static readonly TimeSpan OfferAgainAfter = TimeSpan.FromDays(1);

    /// <summary>
    /// The longest the start waits for its look before the components question of a version change (0.1.55): a newer version is offered
    /// first; no internet or a slow answer never holds the start up longer than this (the answer is then offered after the question).
    /// </summary>
    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(4);

    /// <summary>The longest wait for GitHub's answer.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The argument the restarted app gets: wait until the old process (its id follows) has ended before starting.</summary>
    public const string AfterUpdateArgument = "--after-update";

    /// <summary>The name the replaced program file gets next to the new one (Windows; removed at the next start).</summary>
    public const string OldSuffix = ".old";

    /// <summary>The folder next to the program where a new version is downloaded and unpacked before it replaces the program.</summary>
    public const string StagingFolder = ".tm-concept-update";
}
