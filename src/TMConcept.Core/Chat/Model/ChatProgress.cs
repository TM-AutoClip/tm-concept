namespace TMConcept.Core.Chat;

/// <summary>
/// The progress of one chat call, for the screen (<see cref="ChatRequest.Progress"/>): the phase, the time since the request was sent, about
/// how many visible tokens arrived (the characters / 4; the exact count comes only with the end of the answer) and the visible text so far
/// (a caller can count the complete items in it). Reported at most a few times a second; a new phase is reported at once.
/// </summary>
public sealed record ChatProgress(ChatPhase Phase, TimeSpan Elapsed, int ApproxTokens, string Text);
