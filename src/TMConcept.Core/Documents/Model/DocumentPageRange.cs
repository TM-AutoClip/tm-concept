using System.Globalization;

namespace TMConcept.Core.Documents;

/// <summary>
/// Pages <see cref="From"/>–<see cref="To"/> of a PDF (1-based, both included; 0.1.24): what an add reads, or what a concept run uses. Written
/// as "120-180", "120" (one page), "120-" (to the end) or "-50" (from the start); empty or "all" means every page (no range).
/// </summary>
public sealed record DocumentPageRange(int From, int To)
{
    /// <summary>Pages in the range.</summary>
    public int Count => To - From + 1;

    /// <summary>Whether <paramref name="page"/> is in the range.</summary>
    public bool Contains(int page) => page >= From && page <= To;

    /// <summary>"120-180" (one page: "120").</summary>
    public override string ToString() => From == To
        ? From.ToString(CultureInfo.InvariantCulture)
        : From.ToString(CultureInfo.InvariantCulture) + "-" + To.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads <paramref name="text"/> for a document of <paramref name="pages"/> pages (0 = unknown: no upper check). True with a null
    /// <paramref name="range"/> for every page (empty, "all", "tümü", or a range that covers the whole document); false when it is not a
    /// range of this document (letters, a page past the end, a start after the end).
    /// </summary>
    public static bool TryParse(string? text, int pages, out DocumentPageRange? range)
    {
        range = null;
        var value = (text ?? string.Empty).Trim().Replace('–', '-').Replace('—', '-').Replace(" ", string.Empty, StringComparison.Ordinal);
        if (value.Length == 0 || value.Equals("all", StringComparison.OrdinalIgnoreCase) || value.Equals("tümü", StringComparison.OrdinalIgnoreCase)
            || value.Equals("tumu", StringComparison.OrdinalIgnoreCase) || value == "*")
        {
            return true;
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var left = dash < 0 ? value : value[..dash];
        var right = dash < 0 ? value : value[(dash + 1)..];
        if (!Page(left, 1, out var from) || !Page(right, pages > 0 ? pages : int.MaxValue, out var to))
        {
            return false;
        }

        if (from < 1 || to < from || (pages > 0 && to > pages))
        {
            return false;
        }

        if (pages > 0 && from == 1 && to == pages)
        {
            return true;
        }

        range = new DocumentPageRange(from, to);
        return true;
    }

    private static bool Page(string text, int empty, out int page)
    {
        if (text.Length == 0)
        {
            page = empty;
            return empty != int.MaxValue;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page);
    }
}
