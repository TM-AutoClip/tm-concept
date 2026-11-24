using Avalonia.Controls;

namespace TMConcept.Desktop.Composition;

/// <summary>
/// Maps <typeparamref name="TViewModel"/> to <typeparamref name="TView"/>; <paramref name="cache"/> keeps one view per view model
/// (pages and settings sections, <see cref="ViewLocator"/>).
/// </summary>
public sealed class ViewRegistration<TViewModel, TView>(bool cache = false) : IViewRegistration
    where TView : Control, new()
{
    /// <inheritdoc />
    public void Register(ViewLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        locator.Register<TViewModel, TView>(cache);
    }
}
