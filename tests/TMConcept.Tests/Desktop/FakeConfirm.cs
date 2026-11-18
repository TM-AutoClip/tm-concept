using TMConcept.Desktop.Dialogs;

namespace TMConcept.Tests.Desktop;

/// <summary>Answers every confirmation from <see cref="Answers"/> (in order), then with <see cref="Answer"/>, and records the questions.</summary>
internal sealed class FakeConfirm : IConfirmDialog
{
    public bool Answer { get; set; } = true;

    public Queue<bool> Answers { get; } = new();

    public bool Checked { get; set; }

    public List<string> Asked { get; } = [];

    public List<string> Messages { get; } = [];

    public List<bool> Dangers { get; } = [];

    /// <summary>Runs while the question is open (something that happens while the user reads it).</summary>
    public Action? OnAsk { get; set; }

    public Task<ConfirmResult> AskAsync(string title, string message, string confirmText, bool danger, string? checkbox = null, string? cancelText = null)
    {
        Asked.Add(title);
        Messages.Add(message);
        Dangers.Add(danger);
        OnAsk?.Invoke();
        return Task.FromResult(new ConfirmResult(Answers.Count > 0 ? Answers.Dequeue() : Answer, Checked));
    }
}
