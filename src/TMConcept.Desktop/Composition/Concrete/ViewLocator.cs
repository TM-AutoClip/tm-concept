using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using TMConcept.Core.Diagnostics;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Composition;

/// <summary>
/// Maps view models to views from an explicit list (no reflection by name). Building a page's view is timed (<c>page.&lt;name&gt;.view</c>
/// in the log); the many small views inside a page are not.
/// <para>
/// 0.1.40 (the developer: "program hantal"): a mapping registered with <c>cache: true</c> (the pages and the settings sections, one
/// view model each for the whole run) builds its view once per view model and gives the same view back on the next visit, so a page
/// change does not build the page again (the concepts page's view took about 100 ms each time). The views of items, of which a list has
/// many short-lived view models, are not cached. A cached view still sitting under another parent (another window that showed the same
/// view model) is taken out of a panel there; under anything else a new view is built for the new place, so a view never has two parents.
/// </para>
/// </summary>
public sealed class ViewLocator(IStepTimer timer) : IDataTemplate
{
    private readonly Dictionary<Type, (Func<Control> Create, bool Cache)> _views = [];
    private readonly ConditionalWeakTable<object, Control> _built = [];

    /// <summary>
    /// Adds a mapping. <paramref name="cache"/>: one view per view model, kept and used again (only for view models that live as long as
    /// the app, such as pages and settings sections; never for list items).
    /// </summary>
    public ViewLocator Register<TViewModel, TView>(bool cache = false)
        where TView : Control, new()
    {
        _views[typeof(TViewModel)] = (() => new TView(), cache);
        return this;
    }

    /// <inheritdoc />
    public Control? Build(object? param)
    {
        if (param is null || !_views.TryGetValue(param.GetType(), out var view))
        {
            return new TextBlock { Text = "No view for " + param?.GetType().Name };
        }

        if (view.Cache && _built.TryGetValue(param, out var kept) && Free(kept))
        {
            if (param is PageViewModelBase shown)
            {
                // "page.<name>.view 0": the page came back without being built again.
                timer.Record("page." + shown.TimingName + ".view", TimeSpan.Zero);
            }

            return kept;
        }

        var built = Create(param, view.Create);
        if (view.Cache)
        {
            _built.AddOrUpdate(param, built);
        }

        return built;
    }

    /// <inheritdoc />
    public bool Match(object? data) => data is not null && _views.ContainsKey(data.GetType());

    /// <summary>Whether a kept view can be placed again (it has no parent, or it was taken out of the panel that held it).</summary>
    private static bool Free(Control view)
    {
        if (view.Parent is null && view.GetVisualParent() is null)
        {
            return true;
        }

        if (view.Parent is Panel panel && panel.Children.Remove(view))
        {
            return view.GetVisualParent() is null;
        }

        return false;
    }

    private Control Create(object param, Func<Control> create)
    {
        if (param is not PageViewModelBase page)
        {
            return create();
        }

        using (timer.Step("page." + page.TimingName + ".view"))
        {
            return create();
        }
    }
}
