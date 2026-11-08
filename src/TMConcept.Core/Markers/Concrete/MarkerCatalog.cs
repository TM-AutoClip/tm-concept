using TMConcept.Core.Scenes;
using TMConcept.Core.Templates;

namespace TMConcept.Core.Markers;

/// <summary>
/// The placeholders of the drop-downs. Applied by the program itself, exactly, whatever a model writes: a pause is silence in the voice and
/// lengthens its scene; a speed marker changes the voice's speed; a soft transition is a short fade through black that ffmpeg adds after the
/// render. The one exception is the hard cut: it is only told to the video code (as the scene's "transition" in the input), so it depends on
/// that code — the help text says so, and whether the program should force it (or drop it) is the developer's open question. Nothing else
/// is sent to a model as a request: what cannot be applied (emphasis on screen, a big number…) is not offered.
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
        Pause("pause-0.5", T("[duraklama 0,5 sn]", "[pause 0.5 s]"), 500),
        Pause("pause-1", T("[duraklama 1 sn]", "[pause 1 s]"), 1000),
        Pause("pause-2", T("[duraklama 2 sn]", "[pause 2 s]"), 2000),
        Pace(Slow, T("[yavaş]", "[slow]"), T("sonrası daha yavaş okunur", "what follows is read slower"), SlowSpeed),
        Pace(Fast, T("[hızlı]", "[fast]"), T("sonrası daha hızlı okunur", "what follows is read faster"), FastSpeed),
        Pace("normal-speed", T("[normal hız]", "[normal speed]"), T("sesin kendi hızına dön", "back to the voice's own speed"), 1),
        Transition("hard-cut", T("[sert geçiş]", "[hard cut]"), T("video koduna ani kesme diye söylenir", "passed on to the video code as a sudden cut"), SceneTransitions.Cut),
        Transition("soft-transition", T("[yumuşak geçiş]", "[soft transition]"), T("sahneye kısa bir kararmayla geçilir", "the scene starts after a short fade through black"),
            SceneTransitions.Fade),
    ];

    /// <summary>The markers of one kind.</summary>
    public static IReadOnlyList<Marker> Of(MarkerKind kind) => All.Where(m => m.Kind == kind).ToList();

    private static LocalizedText T(string tr, string en) => new() { Tr = tr, En = en };

    private static Marker Pause(string id, LocalizedText token, int milliseconds) =>
        new(id, MarkerKind.Narration, token, T("araya sessizlik eklenir", "silence is added there")) { PauseMs = milliseconds };

    private static Marker Pace(string id, LocalizedText token, LocalizedText hint, double speed) => new(id, MarkerKind.Narration, token, hint) { Speed = speed };

    private static Marker Transition(string id, LocalizedText token, LocalizedText hint, string transition) =>
        new(id, MarkerKind.Transition, token, hint) { Transition = transition };
}
