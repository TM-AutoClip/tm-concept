using TMConcept.Core.Concepts;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.Concepts;

/// <summary>
/// The findings of <see cref="ConceptSceneCheck"/> in the user's language ("2 scenes, at least 4 needed; scene 1 has no on-screen line"), for
/// the yellow line of a concept and the question before approving it. Empty when the concept has none.
/// </summary>
public static class SceneWarnings
{
    /// <summary>The findings joined; empty = none.</summary>
    public static string Text(Localizer text, IReadOnlyList<SceneProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(problems);
        return string.Join("; ", problems.Select(p => p.Kind switch
        {
            SceneProblemKind.TooFew => text.Format("concepts.scenes.few", p.Have, p.Need),
            SceneProblemKind.EmptyVisual => text.Format("concepts.scenes.empty", p.Scene),
            _ => text.Format("concepts.scenes.long", p.Scene, p.Have, p.Need),
        }));
    }

    /// <summary>The yellow line of a concept whose scenes fail the check (empty when they pass or no profile is open).</summary>
    public static string Line(Localizer text, string? content, string language, Core.Profiles.Profile? profile)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (profile is null)
        {
            return string.Empty;
        }

        var found = Text(text, ConceptSceneCheck.Check(content, language.Length > 0 ? language : profile.ContentLanguage, profile.TargetSeconds));
        return found.Length == 0 ? string.Empty : text.Format("concepts.scenes.warning", found);
    }
}
