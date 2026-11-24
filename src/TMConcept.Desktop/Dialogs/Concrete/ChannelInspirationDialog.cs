using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary><see cref="IChannelInspirationDialog"/> as a modal window; a new view model every time, disposed (look canceled, temporary files deleted) when it closes.</summary>
public sealed class ChannelInspirationDialog(WindowHost host, Func<ChannelInspirationViewModel> models, ILogger<ChannelInspirationDialog> logger)
    : IChannelInspirationDialog
{
    /// <inheritdoc />
    public async Task ShowAsync(Func<ChannelProfileDraft, Task<string?>> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (host.Active is not { } owner)
        {
            logger.LogWarning("Kanaldan esinlen was asked for without a window");
            return;
        }

        using var model = models();
        model.Create = create;
        logger.LogInformation("Kanaldan esinlen opened");
        var window = new ChannelInspirationWindow { DataContext = model };
        _ = model.LoadAsync();
        await window.ShowDialog(owner).ConfigureAwait(true);
        logger.LogInformation("Kanaldan esinlen closed");
    }
}
