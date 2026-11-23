using Avalonia.Controls;
using Avalonia.Controls.Templates;
using TMConcept.Core.Diagnostics;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Composition;

/// <summary>
/// Maps view models to views from an explicit list (no reflection by name). Building a page's view is timed (<c>page.&lt;name&gt;.view</c>
/// in the log); the many small views inside a page are not.
/// </summary>
public sealed class ViewLocator(IStepTimer timer) : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _views = [];

    /// <summary>Adds a mapping.</summary>
    public ViewLocator Register<TViewModel, TView>()
        where TView : Control, new()
    {
        _views[typeof(TViewModel)] = () => new TView();
        return this;
    }

    /// <inheritdoc />
    public Control? Build(object? param)
    {
        if (param is null || !_views.TryGetValue(param.GetType(), out var create))
        {
            return new TextBlock { Text = "No view for " + param?.GetType().Name };
        }

        if (param is not PageViewModelBase page)
        {
            return create();
        }

        using (timer.Step("page." + page.TimingName + ".view"))
        {
            return create();
        }
    }

    /// <inheritdoc />
    public bool Match(object? data) => data is not null && _views.ContainsKey(data.GetType());
}
