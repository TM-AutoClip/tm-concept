namespace TMConcept.Desktop.Reports;

/// <summary>Rules of "Report a problem": where an issue is opened, how the logs zip is named and how many are kept.</summary>
public static class ReportRules
{
    /// <summary>The page that opens a new issue of the app's repository.</summary>
    public const string NewIssueAddress = "https://github.com/TM-AutoClip/tm-concept/issues/new";

    /// <summary>The issue form (<c>.github/ISSUE_TEMPLATE/bug_report.yml</c>).</summary>
    public const string Template = "bug_report.yml";

    /// <summary>Name prefix of a logs zip (<c>tm-concept-logs-yyyyMMdd-HHmm.zip</c>).</summary>
    public const string ZipPrefix = "tm-concept-logs-";

    /// <summary>The system information file inside the zip.</summary>
    public const string SystemFile = "system.txt";

    /// <summary>Report zips kept in the reports folder (older ones are deleted when a new one is made).</summary>
    public const int KeepZips = 5;

    /// <summary>Longest issue address (GitHub refuses much longer ones; browsers cut at about 8000).</summary>
    public const int MaxAddressLength = 8000;

    /// <summary>Longest pre-filled value of one form field.</summary>
    public const int MaxFieldLength = 300;
}
