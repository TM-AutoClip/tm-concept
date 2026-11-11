using TMConcept.Core.Chat;

namespace TMConcept.Core.Videos;

/// <summary>
/// Progress of one video run for the screen: the share 0–1 of the whole run, the running step (<see cref="VideoSteps"/>; empty after the
/// last one), the engine's own line for the render (its text as the engine wrote it; no screen text is made in Core), what the run has paid
/// so far, the steps it does in order (<see cref="Plan"/>; a step that is not in it is skipped), the scenes done of all for the narration
/// and the images, and the code call's own progress while the code model writes (phase, about how many tokens).
/// </summary>
public sealed record VideoProgress(double Share, string Step, string Detail, decimal SpentUsd, IReadOnlyList<string> Plan, int Done = 0, int Total = 0,
    ChatProgress? Call = null);
