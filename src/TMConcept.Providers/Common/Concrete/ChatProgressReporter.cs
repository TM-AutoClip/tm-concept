using TMConcept.Core.Chat;

namespace TMConcept.Providers.Common;

/// <summary>
/// Feeds a call's <see cref="ChatRequest.Progress"/> from the stream: <see cref="Sent"/> before the request, <see cref="Line"/> for every line
/// that arrives (no visible text yet = the model thinks, then it writes), <see cref="Done"/> when the answer is complete. A new phase is
/// reported at once; while the model writes at most every <see cref="Gap"/> (4 a second), so a fast stream does not flood the screen.
/// Nothing is done without a progress.
/// </summary>
public sealed class ChatProgressReporter(IProgress<ChatProgress>? target, TimeProvider clock)
{
    /// <summary>Shortest time between two "writing" reports.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(250);

    private long _started = clock.GetTimestamp();
    private ChatPhase? _phase;
    private TimeSpan _last;

    /// <summary>The request is sent (again after a retry: the time starts anew).</summary>
    public void Sent()
    {
        if (target is null)
        {
            return;
        }

        _started = clock.GetTimestamp();
        _phase = null;
        Report(ChatPhase.Sent, string.Empty);
    }

    /// <summary>A line of the answer arrived; <paramref name="textLength"/> is the visible text so far, <paramref name="text"/> gives it.</summary>
    public void Line(int textLength, Func<string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (target is null)
        {
            return;
        }

        if (textLength == 0)
        {
            if (_phase is null or ChatPhase.Sent)
            {
                Report(ChatPhase.Thinking, string.Empty);
            }

            return;
        }

        if (_phase != ChatPhase.Writing || Elapsed() - _last >= Gap)
        {
            Report(ChatPhase.Writing, text());
        }
    }

    /// <summary>The whole answer arrived (<paramref name="text"/>: its visible text).</summary>
    public void Done(string text)
    {
        if (target is not null)
        {
            Report(ChatPhase.Done, text ?? string.Empty);
        }
    }

    private TimeSpan Elapsed() => clock.GetElapsedTime(_started);

    private void Report(ChatPhase phase, string text)
    {
        _phase = phase;
        _last = Elapsed();
        target!.Report(new ChatProgress(phase, _last, (text.Length + 3) / 4, text));
    }
}
