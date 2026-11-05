namespace TMConcept.Core.Profiles;

/// <summary>Languages concepts and narration can be written in. A new content language = a prompt file + template texts + a voice.</summary>
public static class ContentLanguages
{
    /// <summary>Turkish.</summary>
    public const string Turkish = "tr";

    /// <summary>English.</summary>
    public const string English = "en";

    /// <summary>Every supported content language.</summary>
    public static readonly IReadOnlyList<string> All = [Turkish, English];
}
