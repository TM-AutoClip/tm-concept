using Avalonia.Controls;

namespace TMConcept.Desktop.Composition;

/// <summary>Maps <typeparamref name="TViewModel"/> to <typeparamref name="TView"/>.</summary>
public sealed class ViewRegistration<TViewModel, TView> : IViewRegistration
    where TView : Control, new()
{
    /// <inheritdoc />
    public void Register(ViewLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        locator.Register<TViewModel, TView>();
    }
}
