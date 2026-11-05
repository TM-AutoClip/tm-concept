using TMConcept.Desktop.Dialogs;

namespace TMConcept.Tests.Desktop;

/// <summary>Answers every confirmation with <see cref="Answer"/> and records the questions.</summary>
internal sealed class FakeConfirm : IConfirmDialog
{
    public bool Answer { get; set; } = true;

    public bool Checked { get; set; }

    public List<string> Asked { get; } = [];

    public Task<ConfirmResult> AskAsync(string title, string message, string confirmText, bool danger, string? checkbox = null)
    {
        Asked.Add(title);
        return Task.FromResult(new ConfirmResult(Answer, Checked));
    }
}
