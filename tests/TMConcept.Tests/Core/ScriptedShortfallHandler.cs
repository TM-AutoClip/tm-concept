using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>
/// Answers the memory question from a script (the last answer repeats) and keeps every question with the clock's time when it was asked.
/// With <see cref="Hold"/> it does not answer until the token is canceled (an open dialog).
/// </summary>
internal sealed class ScriptedShortfallHandler(params MemoryShortfallChoice[] answers) : IMemoryShortfallHandler
{
    private readonly Queue<MemoryShortfallChoice> _answers = new(answers);
    private MemoryShortfallChoice _last = answers.Length > 0 ? answers[^1] : MemoryShortfallChoice.Cancel;

    public List<MemoryShortfall> Asked { get; } = [];

    public List<DateTimeOffset> AskedAt { get; } = [];

    public TimeProvider Clock { get; init; } = TimeProvider.System;

    public bool Hold { get; init; }

    /// <summary>Runs when a question comes, before it is answered (the user closes something meanwhile).</summary>
    public Action? OnAsk { get; set; }

    public async Task<MemoryShortfallChoice> AskAsync(MemoryShortfall shortfall, CancellationToken cancellationToken)
    {
        Asked.Add(shortfall);
        AskedAt.Add(Clock.GetUtcNow());
        OnAsk?.Invoke();
        if (Hold)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (_answers.Count > 0)
        {
            _last = _answers.Dequeue();
        }

        return _last;
    }
}
