using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary><see cref="IUpdateDialog"/> as a small modal window over the active window (none = later).</summary>
public sealed class UpdateDialog(WindowHost host, Localizer text) : IUpdateDialog
{
    /// <inheritdoc />
    public async Task<UpdateChoice> AskAsync(string from, string to, string toolsFolder, long toolsBytes)
    {
        if (host.Active is not { } owner)
        {
            return UpdateChoice.Later;
        }

        var model = new UpdateViewModel(Title(text, from, to), text.Format("update.message", toolsFolder, Localization.ByteSizeText.Of(toolsBytes, text.Culture)));
        await new UpdateWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Choice;
    }

    /// <summary>The window's title: from which version to which (only the new one when the old one is not known).</summary>
    public static string Title(Localizer text, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.IsNullOrEmpty(from) ? text.Format("update.title.unknown", to) : text.Format("update.title", from, to);
    }
}
