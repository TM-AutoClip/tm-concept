using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Dialogs;

/// <summary>Shows the "Kanaldan esinlen" window (0.1.53) over the active window.</summary>
public interface IChannelInspirationDialog
{
    /// <summary>
    /// Shows it and returns when it is closed. "Profili oluştur" calls <paramref name="create"/> with the edited draft: null = the profile was
    /// made, otherwise the reason it was not (shown in the window). The look's temporary files are deleted when the window closes.
    /// </summary>
    Task ShowAsync(Func<ChannelProfileDraft, Task<string?>> create);
}
