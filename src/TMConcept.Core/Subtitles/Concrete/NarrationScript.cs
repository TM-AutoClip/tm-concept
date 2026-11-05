using System.Globalization;
using System.Text;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Subtitles;

/// <summary>The narration as a text file (scene number, target length, text) so the user can record the voice-over themselves.</summary>
public static class NarrationScript
{
    /// <summary>The file text.</summary>
    public static string Text(string title, string language, IReadOnlyList<SubtitleSource> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var turkish = language == ContentLanguages.Turkish;
        var text = new StringBuilder(title).Append("\n\n");
        for (var i = 0; i < scenes.Count; i++)
        {
            var seconds = (scenes[i].DurationMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
            text.Append(turkish ? $"Sahne {i + 1} (≈ {seconds} sn)" : $"Scene {i + 1} (≈ {seconds} s)").Append('\n')
                .Append(scenes[i].Text.Trim()).Append("\n\n");
        }

        return text.ToString();
    }
}
