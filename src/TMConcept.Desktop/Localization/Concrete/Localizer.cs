using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Localization;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Localization;

/// <summary>
/// Interface texts from the embedded <c>Lang/&lt;code&gt;.json</c> files; the languages are discovered from the files (a new language = a new
/// file). A missing key falls back to English and is logged once; a missing English key shows the key (the parity test keeps the files
/// complete). The language changes while the app runs (<see cref="LanguageChanged"/>; views read texts as dynamic resources).
/// </summary>
public sealed class Localizer : ILocalizer
{
    private const string Prefix = "TMConcept.Lang.";
    private readonly Dictionary<string, string> _english = Load("en");
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly ILogger<Localizer>? _logger;
    private Dictionary<string, string> _current;

    /// <summary>Loads English.</summary>
    public Localizer(ILogger<Localizer>? logger = null)
    {
        _logger = logger;
        _current = _english;
        Language = "en";
    }

    /// <summary>Raised after the language changed.</summary>
    public event EventHandler? LanguageChanged;

    /// <inheritdoc />
    public string Language { get; private set; }

    /// <summary>Culture of the current language (dates, numbers).</summary>
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "tr" ? "tr-TR" : "en-US");

    /// <summary>Language codes found in the embedded files.</summary>
    public static IReadOnlyList<string> Supported { get; } = typeof(Localizer).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
        .Select(n => n[Prefix.Length..^".json".Length])
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <inheritdoc />
    public string this[string key]
    {
        get
        {
            if (_current.TryGetValue(key, out var value))
            {
                return value;
            }

            if (Language != "en" && _reported.Add(key))
            {
                _logger?.LogWarning("Text key {Key} is missing in {Language}; English is used", key, Language);
            }

            return _english.GetValueOrDefault(key, key);
        }
    }

    /// <summary>Every key with its text in the current language (English where missing).</summary>
    public IReadOnlyDictionary<string, string> Strings
    {
        get
        {
            var all = new Dictionary<string, string>(_english, StringComparer.Ordinal);
            foreach (var (key, value) in _current)
            {
                all[key] = value;
            }

            return all;
        }
    }

    /// <summary>Switches the language (unknown codes mean English).</summary>
    public void Use(string? language)
    {
        var code = language is not null && Supported.Contains(language) ? language : "en";
        if (code == Language && ReferenceEquals(_current, code == "en" ? _english : _current))
        {
            return;
        }

        Language = code;
        _current = code == "en" ? _english : Load(code);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The language matching the operating system (Turkish system → tr, otherwise en; on Linux from LANG).</summary>
    public static string SystemLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    /// <inheritdoc />
    public string Format(string key, params object[] args) => string.Format(Culture, this[key], args);

    /// <summary>A US dollar amount in the current culture (two decimals, three below one cent).</summary>
    public string Usd(decimal amount) => (amount != 0 && Math.Abs(amount) < 0.01m ? amount.ToString("0.000", Culture) : amount.ToString("0.00", Culture)) + " $";

    /// <summary>All keys of a language file (tests).</summary>
    internal static IReadOnlyCollection<string> Keys(string language) => Load(language).Keys;

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream(Prefix + language + ".json")
            ?? throw new InvalidOperationException($"Language file '{language}' is not embedded.");
        return JsonSerializer.Deserialize(stream, DesktopJsonContext.Default.DictionaryStringString) ?? [];
    }
}
