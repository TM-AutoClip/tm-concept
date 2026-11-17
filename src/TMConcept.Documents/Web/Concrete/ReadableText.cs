using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Documents.Web;

/// <summary>
/// The readable text of a web page as Markdown, without running anything: the title (Open Graph title, else &lt;title&gt;); the body of the
/// longest &lt;article&gt;, else &lt;main&gt;, else &lt;body&gt;; scripts, styles, navigation, headers, footers, forms, side bars and comments
/// left out; headings become "#" lines, list items "- " lines, paragraphs and blocks keep their breaks; entities are decoded. Every pattern
/// has a time limit, so a hostile page cannot hang the app.
/// </summary>
public static partial class ReadableText
{
    /// <summary>The title and the Markdown text of <paramref name="html"/>.</summary>
    public static (string Title, string Text) FromHtml(string html)
    {
        var page = Comment().Replace(html ?? string.Empty, " ");
        var title = Decode(OgTitle().Match(page) is { Success: true } og ? og.Groups[1].Value : TitleTag().Match(page).Groups[1].Value);
        page = Dropped().Replace(page, " ");
        var article = Article().Matches(page).Select(m => m.Groups[1].Value).OrderByDescending(a => a.Length).FirstOrDefault();
        var body = article ?? (Main().Match(page) is { Success: true } main ? main.Groups[1].Value : Body().Match(page) is { Success: true } b ? b.Groups[1].Value : page);
        body = Chrome().Replace(body, " ");
        body = HeadingTag().Replace(body, m => "\n\n" + new string('#', m.Groups[1].Value[0] - '0') + " " + Strip(m.Groups[2].Value).Replace('\n', ' ') + "\n\n");
        body = ListItem().Replace(body, "\n- ");
        body = Break().Replace(body, "\n");
        body = Block().Replace(body, "\n\n");
        var text = Decode(Strip(body));
        var lines = text.Split('\n').Select(l => Spaces().Replace(l, " ").Trim());
        var joined = Blank().Replace(string.Join('\n', lines), "\n\n").Trim();
        return (Spaces().Replace(title, " ").Trim(), joined);
    }

    /// <summary>The charset a page declares in a meta tag; empty when none.</summary>
    public static string DeclaredCharset(string head) => MetaCharset().Match(head ?? string.Empty) is { Success: true } m ? m.Groups[1].Value : string.Empty;

    /// <summary>The text of <paramref name="body"/> in <paramref name="charset"/> (from the header, else the page's meta tag, else UTF-8).</summary>
    public static string Decode(byte[] body, string charset)
    {
        ArgumentNullException.ThrowIfNull(body);
        var name = charset;
        if (name.Length == 0)
        {
            name = DeclaredCharset(Encoding.ASCII.GetString(body, 0, Math.Min(body.Length, 4096)));
        }

        // The old code pages too (windows-1254 and iso-8859-9 of older Turkish pages); an unknown name is read as UTF-8.
        var encoding = name.Length > 0 ? Extractors.LegacyEncodings.Named(name) ?? Encoding.UTF8 : Encoding.UTF8;
        return encoding.GetString(body);
    }

    private static string Strip(string html) => Tag().Replace(html, " ");

    private static string Decode(string text) => WebUtility.HtmlDecode(text ?? string.Empty);

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline, 2000)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<meta[^>]+property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex OgTitle();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<(script|style|noscript|svg|template|iframe|object|canvas|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 5000)]
    private static partial Regex Dropped();

    [GeneratedRegex(@"<(nav|header|footer|aside|form|button|select|figure)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 5000)]
    private static partial Regex Chrome();

    [GeneratedRegex(@"<article\b[^>]*>(.*?)</article\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 5000)]
    private static partial Regex Article();

    [GeneratedRegex(@"<main\b[^>]*>(.*?)</main\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 5000)]
    private static partial Regex Main();

    [GeneratedRegex(@"<body\b[^>]*>(.*?)(</body\s*>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline, 5000)]
    private static partial Regex Body();

    [GeneratedRegex(@"<h([1-6])\b[^>]*>(.*?)</h\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 5000)]
    private static partial Regex HeadingTag();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Break();

    [GeneratedRegex(@"</?(p|div|section|blockquote|tr|table|ul|ol|dl|dt|dd|pre|hr)\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex Block();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, 2000)]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t ]+", RegexOptions.None, 2000)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}", RegexOptions.None, 2000)]
    private static partial Regex Blank();

    [GeneratedRegex(@"<meta[^>]+charset\s*=\s*[""']?([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex MetaCharset();
}
