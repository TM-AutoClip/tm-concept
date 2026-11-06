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

    public Task<ConfirmResult> AskAsync(string title, string message, string confirmText, bool danger, string? checkbox = null)
    {
        Asked.Add(title);
        Messages.Add(message);
        return Task.FromResult(new ConfirmResult(Answers.Count > 0 ? Answers.Dequeue() : Answer, Checked));
    }
}
