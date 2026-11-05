namespace TMConcept.Core.Localization;

/// <summary>Interface texts in the chosen language (English and Turkish files; a missing key falls back to English).</summary>
public interface ILocalizer
{
    /// <summary>Current interface language code (<c>en</c> / <c>tr</c>).</summary>
    string Language { get; }

    /// <summary>The text of a key.</summary>
    string this[string key] { get; }

    /// <summary>The text of a key with arguments (current culture).</summary>
    string Format(string key, params object[] args);
}
