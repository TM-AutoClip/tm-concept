using System.Text;
using Shouldly;
using TMConcept.Speech.Chatterbox;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>Chatterbox's own parts that need no model: the tokenizer, the text preparation and the sampler.</summary>
public sealed class ChatterboxTextTests
{
    // A tokenizer.json of the same kind as the model's: BPE over letters, added tokens ([SPACE] normalized), the template of V3.
    private const string Tokenizer = """
        {
          "added_tokens": [
            { "id": 0, "content": "[STOP]", "normalized": false },
            { "id": 1, "content": "[UNK]", "normalized": false },
            { "id": 2, "content": "[SPACE]", "normalized": true },
            { "id": 255, "content": "[START]", "normalized": false },
            { "id": 704, "content": "[tr]", "normalized": false },
            { "id": 705, "content": "[en]", "normalized": false }
          ],
          "normalizer": { "type": "Sequence", "normalizers": [ { "type": "NFKD" } ] },
          "pre_tokenizer": { "type": "Whitespace" },
          "post_processor": {
            "type": "TemplateProcessing",
            "single": [ { "SpecialToken": { "id": "EXAGGERATION" } }, { "SpecialToken": { "id": "BOS" } }, { "Sequence": { "id": "A" } },
              { "SpecialToken": { "id": "EOS" } }, { "SpecialToken": { "id": "START_SPEECH" } }, { "SpecialToken": { "id": "START_SPEECH" } } ],
            "special_tokens": {
              "BOS": { "id": "BOS", "ids": [255] }, "EOS": { "id": "EOS", "ids": [0] },
              "EXAGGERATION": { "id": "EXAGGERATION", "ids": [6563] }, "START_SPEECH": { "id": "START_SPEECH", "ids": [6561] }
            }
          },
          "model": {
            "type": "BPE", "unk_token": "[UNK]",
            "vocab": { "[STOP]": 0, "[UNK]": 1, "[SPACE]": 2, ".": 9, "a": 14, "e": 18, "i": 22, "k": 24, "l": 25, "n": 27, "s": 32, "V": 60,
              "I": 61, "ı": 779, "̧": 766, "̇": 773, "ki": 300, "kin": 301, "Vi": 302, "Vikin": 303 },
            "merges": [ "k i", "ki n", "V i", "Vi kin" ]
          }
        }
        """;

    private static ChatterboxTokenizer Load(TempPaths paths)
    {
        Directory.CreateDirectory(paths.WorkDirectory);
        var file = Path.Combine(paths.WorkDirectory, "tokenizer.json");
        File.WriteAllText(file, Tokenizer, new UTF8Encoding(false));
        return ChatterboxTokenizer.Load(file);
    }

    [Fact]
    public void TokensHaveTheLanguageSpacesMergesAndTheTemplate()
    {
        using var paths = new TempPaths();
        var tokenizer = Load(paths);

        // <EXAGGERATION> <s> [tr] Vikin [SPACE] a . </s> <START_SPEECH> <START_SPEECH>; "Vikin" by the merge ranks.
        tokenizer.Encode("Vikin a.", "tr").ShouldBe([6563, 255, 704, 303, 2, 14, 9, 0, 6561, 6561]);
    }

    [Fact]
    public void TurkishLettersAreDecomposedAndNothingIsLowercased()
    {
        using var paths = new TempPaths();
        var tokenizer = Load(paths);

        // "İ" = I + U+0307, "ş" = s + U+0327 after NFKD; "ı" has no decomposition; the capital I stays (V3: full case).
        tokenizer.Encode("İş Iı", "tr")[3..^3].ShouldBe([61, 773, 32, 766, 2, 61, 779]);

        // A character outside the vocabulary is the unknown token.
        tokenizer.Encode("x", "en")[3..^3].ShouldBe([1]);
    }

    [Fact]
    public void TheWhitespacePreTokenizerKeepsMarksInTheWord()
    {
        ChatterboxTokenizer.Words("aşk, ne?!").ShouldBe(["aşk", ",", "ne", "?!"]);
    }

    [Fact]
    public void PunctuationIsNormalizedAsUpstreamDoes()
    {
        ChatterboxText.Normalize("  bir   gün…   ve “son”: bitti", "en").ShouldBe("Bir gün,  ve \"son\", bitti.");
        ChatterboxText.Normalize("istanbul", "tr").ShouldBe("İstanbul.", "a Turkish i gets its own capital");
        ChatterboxText.Normalize("istanbul", "en").ShouldBe("Istanbul.");
        ChatterboxText.Normalize("Done!", "en").ShouldBe("Done!");
    }

    [Fact]
    public void LongScenesAreSpokenInPartsAtSentenceEnds()
    {
        var sentence = "Bu cümle tam olarak altmış karakterden oluşan bir örnek cümle.";
        var text = string.Join(' ', Enumerable.Repeat(sentence, 12));

        var parts = ChatterboxText.Parts(text, "tr");

        parts.Count.ShouldBeGreaterThan(1);
        parts.ShouldAllBe(p => p.Length <= ChatterboxModel.MaxPartCharacters);
        parts.ShouldAllBe(p => p.EndsWith('.'));
        string.Join(' ', parts).ShouldBe(text);
        ChatterboxText.Parts("Kısa bir sahne.", "tr").ShouldBe(["Kısa bir sahne."]);

        // One sentence longer than a part is cut at a comma.
        var longOne = string.Join(", ", Enumerable.Repeat("uzun bir yan cümle daha", 20)) + ".";
        ChatterboxText.Parts(longOne, "tr").ShouldAllBe(p => p.Length <= ChatterboxModel.MaxPartCharacters);
    }

    [Fact]
    public void TheSamplerPenalizesRepeatsAndDropsUnlikelyTokens()
    {
        // Greedy-like: without randomness left after min-p, the clear winner is taken.
        var sampler = new ChatterboxSampler(1.2f, 1f, 0.9f, 1f, 1);
        sampler.Next([0f, 10f, 0f]).ShouldBe(1);

        // Token 1 was picked: its score 10 / 1.2 ≈ 8.3 loses to 9 now.
        sampler.Next([0f, 10f, 9f]).ShouldBe(2);

        // The same seed gives the same draws.
        var a = new ChatterboxSampler(1.2f, 0.8f, 0.05f, 1f, 7);
        var b = new ChatterboxSampler(1.2f, 0.8f, 0.05f, 1f, 7);
        float[] logits = [1f, 1.1f, 0.9f, 1.05f];
        Enumerable.Range(0, 20).Select(_ => a.Next(logits)).ShouldBe(Enumerable.Range(0, 20).Select(_ => b.Next(logits)));
    }
}
