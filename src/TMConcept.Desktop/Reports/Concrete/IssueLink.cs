using System.Text;

namespace TMConcept.Desktop.Reports;

/// <summary>
/// The address of a new GitHub issue with the bug report form and its version, operating system and architecture fields filled in (GitHub
/// fills an issue form field from the query parameter with the field's id). Every value is URL-escaped and cut, so the address stays far below
/// <see cref="ReportRules.MaxAddressLength"/>. Nothing is sent: the user's browser opens the form and the user submits it.
/// </summary>
public static class IssueLink
{
    /// <summary>The address for <paramref name="fields"/> (field id → value; empty values are left out).</summary>
    public static Uri Build(IReadOnlyList<(string Id, string Value)> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var address = new StringBuilder(ReportRules.NewIssueAddress).Append("?template=").Append(Uri.EscapeDataString(ReportRules.Template));
        foreach (var (id, value) in fields)
        {
            var clean = (value ?? string.Empty).Trim();
            if (clean.Length == 0)
            {
                continue;
            }

            if (clean.Length > ReportRules.MaxFieldLength)
            {
                clean = clean[..ReportRules.MaxFieldLength];
            }

            var part = "&" + Uri.EscapeDataString(id) + "=" + Uri.EscapeDataString(clean);
            if (address.Length + part.Length > ReportRules.MaxAddressLength)
            {
                break;
            }

            address.Append(part);
        }

        return new Uri(address.ToString());
    }

    /// <summary>The address with this program's version, operating system and architecture.</summary>
    public static Uri ForThisProgram() => Build(
    [
        ("version", Logging.AppIdentity.Version),
        ("os", Logging.AppIdentity.Os),
        ("architecture", Logging.AppIdentity.Architecture),
    ]);
}
