namespace TMConcept.Core.Music;

/// <summary>
/// The licences an outside track may have (0.1.45, S6; pure): CC0, CC BY and CC BY-SA allow commercial use with changes. A non-commercial
/// (NC) or no-derivatives (ND) licence, the public domain mark and anything unknown are refused, whatever a search service said.
/// </summary>
public static class MusicLicenses
{
    /// <summary>Whether the Openverse licence code (<c>cc0</c>, <c>by</c>, <c>by-sa</c>, <c>by-nc</c>…) is allowed.</summary>
    public static bool Allowed(string? code)
    {
        var clean = (code ?? string.Empty).Trim().ToLowerInvariant();
        return OpenverseRules.Allowed.Contains(clean, StringComparer.Ordinal);
    }

    /// <summary>The licence as people read it: "CC0 1.0", "CC BY 4.0", "CC BY-SA 3.0".</summary>
    public static string Label(string code, string? version)
    {
        ArgumentNullException.ThrowIfNull(code);
        var clean = code.Trim().ToLowerInvariant();
        var name = clean == "cc0" ? "CC0" : "CC " + clean.ToUpperInvariant();
        var v = (version ?? string.Empty).Trim();
        return v.Length > 0 ? name + " " + v : name;
    }

    /// <summary>The licence asks for credit (every CC BY licence; CC0 does not).</summary>
    public static bool AsksCredit(string label) =>
        (label ?? string.Empty).Trim().Replace('-', ' ').StartsWith(Videos.AssetLicenses.CcByPrefix, StringComparison.OrdinalIgnoreCase);
}
