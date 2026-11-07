using TMConcept.Core.Templates;

namespace TMConcept.Core.Markers;

/// <summary>
/// The placeholders of the drop-downs. Prompt markers go to the model as style requests with the meaning written here (the concept
/// prompt explains the ones a text uses); narration markers are applied on this computer when a new version's voice is made (a pause is
/// silence, a speed marker changes the voice's speed). Visual ones (transitions, emphasis on screen) need new video code, so they are
/// prompt markers only: in the narration editor they would do nothing.
/// </summary>
public static class MarkerCatalog
{
    /// <summary>Voice speed of <see cref="Slow"/>.</summary>
    public const double SlowSpeed = 0.85;

    /// <summary>Voice speed of <see cref="Fast"/>.</summary>
    public const double FastSpeed = 1.15;

    /// <summary>Id of the slow speed marker.</summary>
    public const string Slow = "slow";

    /// <summary>Id of the fast speed marker.</summary>
    public const string Fast = "fast";

    /// <summary>Every marker, in the order of the drop-downs.</summary>
    public static readonly IReadOnlyList<Marker> All =
    [
        Prompt("hook", T("[merak uyandıran giriş]", "[hook opening]"), T("ilk cümle soru ya da şaşırtıcı bir sayı", "first sentence a question or a surprising number"),
            T("ilk cümle bir soru ya da şaşırtıcı bir sayı olsun.", "open with a question or a surprising number in the first sentence.")),
        Prompt("hard-cut", T("[sert geçiş]", "[hard cut]"), T("sahneler arası ani kesme", "sudden cut between scenes"),
            T("sahneler sert kesmeyle değişsin; sahne satırında bunu söyle (\"sert geçiş: …\").",
                "scenes change with hard cuts; say so in the scene lines (\"hard cut to …\").")),
        Prompt("soft-transition", T("[yumuşak geçiş]", "[soft transition]"), T("sahneler arası yavaş geçiş", "slow cross-fade between scenes"),
            T("sahneler yavaş, yumuşak geçişlerle birbirine aksın; sahne satırında bunu söyle.",
                "scenes flow into each other with slow cross-fades; say so in the scene lines.")),
        Prompt("pause", T("[duraklama]", "[pause]"), T("konuşmasız kısa bir an", "a short moment without speech"),
            T("orada dramatik bir duraklama olsun: arkasından seslendirme gelmeyen bir sahne satırı yaz.",
                "leave a dramatic pause there: write a scene line with no narration after it.")),
        Prompt("emphasis", T("[vurgu]", "[emphasis]"), T("ana fikir, ekranda büyük yazıyla", "the key point, big on screen"),
            T("ardından gelen fikir ana nokta: kısa bir cümleyle söyle ve ekranda büyük yazı olarak göster.",
                "the point that follows is the key point: say it in one short sentence and show it as big on-screen text.")),
        Prompt("map", T("[harita]", "[map]"), T("harita sahneleri", "map scenes"),
            T("harita sahneleri kullan (yerler, yollar, sınırlar).", "use map scenes (places, routes, borders).")),
        Prompt("timeline", T("[zaman çizelgesi]", "[timeline]"), T("olayları sıralayan çizelge", "a timeline of the events"),
            T("olayları sıralayan bir zaman çizelgesi sahnesi kullan.", "use a timeline scene that puts the events in order.")),
        Prompt("comparison", T("[karşılaştırma]", "[comparison]"), T("yan yana karşılaştırma", "side-by-side comparison"),
            T("yan yana bir karşılaştırma sahnesi kullan.", "use a side-by-side comparison scene.")),
        Prompt("big-number", T("[büyük sayı]", "[big number]"), T("önemli sayılar ekranda büyük", "key numbers big on screen"),
            T("önemli sayıları ekranda büyük göster.", "show the key numbers big on screen.")),
        Prompt("ending-question", T("[sonda soru]", "[ending question]"), T("izleyiciye soruyla bitir", "end with a question to the viewer"),
            T("video izleyiciye sorulan bir soruyla bitsin.", "end the video with a question to the viewer.")),
        Prompt("short-sentences", T("[kısa cümleler]", "[short sentences]"), T("en çok ~12 kelimelik cümleler", "sentences of about 12 words at most"),
            T("seslendirme kısa cümlelerle olsun (cümle başına en çok 12 kelime kadar).", "write the narration in short sentences (about 12 words each at most).")),
        Pause("pause-0.5", T("[duraklama 0,5 sn]", "[pause 0.5 s]"), 500),
        Pause("pause-1", T("[duraklama 1 sn]", "[pause 1 s]"), 1000),
        Pause("pause-2", T("[duraklama 2 sn]", "[pause 2 s]"), 2000),
        Pace(Slow, T("[yavaş]", "[slow]"), T("sonrası daha yavaş okunur", "what follows is read slower"), SlowSpeed),
        Pace(Fast, T("[hızlı]", "[fast]"), T("sonrası daha hızlı okunur", "what follows is read faster"), FastSpeed),
        Pace("normal-speed", T("[normal hız]", "[normal speed]"), T("sesin kendi hızına dön", "back to the voice's own speed"), 1),
    ];

    /// <summary>The markers of one kind.</summary>
    public static IReadOnlyList<Marker> Of(MarkerKind kind) => All.Where(m => m.Kind == kind).ToList();

    private static LocalizedText T(string tr, string en) => new() { Tr = tr, En = en };

    private static Marker Prompt(string id, LocalizedText token, LocalizedText hint, LocalizedText meaning) => new(id, MarkerKind.Prompt, token, hint, meaning);

    private static Marker Pause(string id, LocalizedText token, int milliseconds) =>
        new(id, MarkerKind.Narration, token, T("araya sessizlik eklenir", "silence is added there"), T(string.Empty, string.Empty)) { PauseMs = milliseconds };

    private static Marker Pace(string id, LocalizedText token, LocalizedText hint, double speed) =>
        new(id, MarkerKind.Narration, token, hint, T(string.Empty, string.Empty)) { Speed = speed };
}
