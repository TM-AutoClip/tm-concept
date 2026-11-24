using System.Text.Json;

namespace TMConcept.Core.Subtitles;

/// <summary>
/// Reads the JSON file whisper.cpp's whisper-cli writes with <c>--output-json --max-len 1 --split-on-word</c> (0.1.48): one entry of
/// <c>transcription</c> per word, its <c>offsets.from</c> / <c>offsets.to</c> in milliseconds and its <c>text</c> (with a leading space).
/// Entries without a letter or digit (punctuation alone) and whisper's own markers ("[BLANK_AUDIO]", "[_BEG_]", "(music)") are left out.
/// </summary>
public static class WhisperJson
{
    /// <summary>The heard words of <paramref name="json"/>, in order; throws <see cref="FormatException"/> when it is not whisper-cli's JSON.</summary>
    public static IReadOnlyList<RecognizedWord> Words(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("transcription", out var transcription) || transcription.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("The whisper.cpp output has no transcription list.");
            }

            var words = new List<RecognizedWord>();
            foreach (var entry in transcription.EnumerateArray())
            {
                if (!entry.TryGetProperty("offsets", out var offsets) || !entry.TryGetProperty("text", out var textElement)
                    || textElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = textElement.GetString()!.Trim();
                if (text.Length == 0 || Marker(text) || AlignmentText.Fold(text).Length == 0)
                {
                    continue;
                }

                var from = offsets.GetProperty("from").GetInt64();
                var to = offsets.GetProperty("to").GetInt64();
                words.Add(new RecognizedWord(text, from, Math.Max(to, from)));
            }

            return words;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("The whisper.cpp output could not be read: " + ex.Message, ex);
        }
    }

    private static bool Marker(string text) =>
        (text.StartsWith('[') && text.EndsWith(']')) || (text.StartsWith('(') && text.EndsWith(')') && text.Length > 2) || (text.StartsWith('*') && text.EndsWith('*') && text.Length > 2);
}
