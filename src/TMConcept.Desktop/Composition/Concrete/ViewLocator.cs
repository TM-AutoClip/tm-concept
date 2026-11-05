using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace TMConcept.Desktop.Composition;

/// <summary>Maps view models to views from an explicit list (no reflection by name).</summary>
public sealed class ViewLocator : IDataTemplate
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
    public Control? Build(object? param) =>
        param is not null && _views.TryGetValue(param.GetType(), out var create)
            ? create()
            : new TextBlock { Text = "No view for " + param?.GetType().Name };

    /// <inheritdoc />
    public bool Match(object? data) => data is not null && _views.ContainsKey(data.GetType());
}
