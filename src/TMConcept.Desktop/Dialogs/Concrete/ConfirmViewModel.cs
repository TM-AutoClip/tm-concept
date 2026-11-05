using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.Dialogs;

/// <summary>Content of a confirmation window.</summary>
public sealed partial class ConfirmViewModel(string title, string message, string confirmText, string cancelText, bool danger, string? checkbox)
    : ObservableObject
{
    /// <summary>Title.</summary>
    public string Title { get; } = title;

    /// <summary>Question.</summary>
    public string Message { get; } = message;

    /// <summary>Confirm button text.</summary>
    public string ConfirmText { get; } = confirmText;

    /// <summary>Cancel button text.</summary>
    public string CancelText { get; } = cancelText;

    /// <summary>Whether the action is destructive (red button).</summary>
    public bool Danger { get; } = danger;

    /// <summary>Not destructive.</summary>
    public bool Normal => !Danger;

    /// <summary>Check box text (empty = no check box).</summary>
    public string CheckboxText { get; } = checkbox ?? string.Empty;

    /// <summary>Whether a check box is shown.</summary>
    public bool HasCheckbox => CheckboxText.Length > 0;

    /// <summary>Check box state.</summary>
    [ObservableProperty]
    public partial bool Checked { get; set; }

    /// <summary>Whether the user confirmed.</summary>
    public bool Confirmed { get; set; }
}
