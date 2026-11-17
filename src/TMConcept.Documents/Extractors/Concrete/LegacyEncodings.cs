using System.Text;

namespace TMConcept.Documents.Extractors;

/// <summary>
/// The old Windows and ISO code pages (windows-1254 and iso-8859-9 for Turkish, windows-1252, windows-1251, …): .NET knows only the Unicode
/// ones and Latin-1 until <see cref="CodePagesEncodingProvider"/> is registered, which happens once, the first time this class is used (the
/// provider is part of the shared framework). Without it an older Turkish page or a Notepad "ANSI" text file lost its ğ, ş, ı and İ.
/// </summary>
public static class LegacyEncodings
{
    private static readonly Lazy<bool> Registered = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    });

    /// <summary>Windows-1254 (Turkish).</summary>
    public static Encoding Turkish => Get(1254);

    /// <summary>Windows-1252 (Western European).</summary>
    public static Encoding Western => Get(1252);

    /// <summary>The encoding called <paramref name="name"/> ("windows-1254", "iso-8859-9", "utf-8", …); null when there is none by that name.</summary>
    public static Encoding? Named(string name)
    {
        _ = Registered.Value;
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The text of bytes that are not UTF-8 and carry no byte order mark: Windows-1254 when they hold one of the six bytes where it differs
    /// from Windows-1252 (Ğ İ Ş ğ ı ş there; Ð Ý Þ ð ý þ in the Western page, letters a Turkish text has and nearly no other one does),
    /// otherwise Windows-1252 (which also reads Latin-1 text, and gives € and curly quotes their places).
    /// </summary>
    public static string DecodeAnsi(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var turkish = bytes.AsSpan().IndexOfAny([(byte)0xD0, (byte)0xDD, (byte)0xDE, (byte)0xF0, (byte)0xFD, (byte)0xFE]) >= 0;
        return (turkish ? Turkish : Western).GetString(bytes);
    }

    private static Encoding Get(int codePage)
    {
        _ = Registered.Value;
        return Encoding.GetEncoding(codePage);
    }
}
