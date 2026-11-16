namespace TMConcept.Desktop.Dialogs;

/// <summary>Content of the window that follows a new app version (<see cref="IUpdateDialog"/>).</summary>
public sealed class UpdateViewModel(string title, string message)
{
    /// <summary>Title ("TM Concept was updated (0.1.15 → 0.1.16)").</summary>
    public string Title { get; } = title;

    /// <summary>Where the components live and what each choice does.</summary>
    public string Message { get; } = message;

    /// <summary>The choice; <see cref="UpdateChoice.Later"/> until a button is pressed (closing the window is "later").</summary>
    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    /// <summary>Records the pressed button.</summary>
    public void Choose(UpdateChoice choice) => Choice = choice;
}
