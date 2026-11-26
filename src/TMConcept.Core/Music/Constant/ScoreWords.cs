namespace TMConcept.Core.Music;

/// <summary>
/// The words a prompt is read by (0.1.55, <see cref="ScorePromptReader"/>): English and Turkish words, folded like
/// <see cref="Text.TurkishFold"/> does ("hüzünlü" → "huzunlu"), each naming a <see cref="ScoreMood"/>, a <see cref="ScoreStyle"/> or a tempo. A word longer than
/// <see cref="WholeWordLength"/> letters is a word start ("huzun" finds "huzunlu"); a shorter one must be the whole word ("sad" is not
/// "sadece", "buyu" is not "buyuk").
/// </summary>
public static class ScoreWords
{
    /// <summary>Words up to this many letters only count as whole words.</summary>
    public const int WholeWordLength = 4;

    /// <summary>The words of each mood.</summary>
    public static readonly IReadOnlyDictionary<ScoreMood, IReadOnlyList<string>> Moods = new Dictionary<ScoreMood, IReadOnlyList<string>>
    {
        [ScoreMood.Calm] = ["calm", "peace", "relax", "gentle", "soft", "serene", "lofi", "sakin", "huzur", "dingin", "yumusak", "sessiz"],
        [ScoreMood.Uplifting] = ["uplift", "hope", "hopeful", "happy", "bright", "joy", "joyful", "inspir", "positive", "mutlu", "nese", "neseli", "umut", "umutlu",
            "cosku", "iyimser", "sevinc"],
        [ScoreMood.Epic] = ["epic", "heroic", "grand", "majestic", "glorious", "epik", "destan", "kahraman", "gorkem", "ihtisam", "muhtesem"],
        [ScoreMood.Dark] = ["dark", "gloom", "ominous", "horror", "sinister", "karanlik", "kasvet", "ugursuz", "korku", "urkutucu"],
        [ScoreMood.Mysterious] = ["myster", "mystic", "magic", "enigma", "gizem", "esrar", "mistik", "buyu", "buyulu", "sihir"],
        [ScoreMood.Playful] = ["playful", "fun", "funny", "quirky", "comic", "cheerful", "eglen", "oyun", "sakaci", "komik", "cocuk"],
        [ScoreMood.Tense] = ["tense", "tension", "suspens", "thrill", "anxious", "gergin", "gerilim", "heyecan", "endise"],
        [ScoreMood.Sad] = ["sad", "melanchol", "sorrow", "grief", "emotional", "huzun", "uzgun", "melankoli", "duygusal", "keder"],
        [ScoreMood.Action] = ["action", "chase", "energetic", "fight", "battle", "aksiyon", "enerjik", "kovala", "savas", "carpisma", "dovus"],
    };

    /// <summary>The words of each style.</summary>
    public static readonly IReadOnlyDictionary<ScoreStyle, IReadOnlyList<string>> Styles = new Dictionary<ScoreStyle, IReadOnlyList<string>>
    {
        [ScoreStyle.Orchestral] = ["orchestr", "symphon", "strings", "classical", "orkestra", "senfoni", "yayli", "klasik"],
        [ScoreStyle.Cinematic] = ["cinemat", "film", "films", "movie", "trailer", "documentary", "sinematik", "belgesel", "fragman"],
        [ScoreStyle.Electronic] = ["electro", "synth", "techno", "edm", "disco", "house", "dance", "elektronik", "disko", "dans", "tekno"],
        [ScoreStyle.Ambient] = ["ambient", "atmospher", "drone", "space", "ambiyans", "atmosfer", "uzay"],
        [ScoreStyle.Acoustic] = ["acoustic", "guitar", "folk", "piano", "akustik", "gitar", "piyano", "halk"],
        [ScoreStyle.World] = ["world", "ethnic", "oriental", "egypt", "arab", "arabic", "persia", "desert", "etnik", "oryantal", "misir", "arap", "anadolu"],
        [ScoreStyle.Medieval] = ["mediev", "renaiss", "castle", "knight", "viking", "byzant", "ottoman", "ortacag", "sato", "sovalye", "bizans", "osmanli"],
        [ScoreStyle.Pop] = ["pop", "radio", "catchy", "radyo"],
    };

    /// <summary>The words of a slow tempo.</summary>
    public static readonly IReadOnlyList<string> Slow = ["slow", "yavas", "agir", "ballad", "balad"];

    /// <summary>The words of a fast tempo.</summary>
    public static readonly IReadOnlyList<string> Fast = ["fast", "quick", "upbeat", "hizli", "tempolu"];
}
