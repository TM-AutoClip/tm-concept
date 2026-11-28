namespace TMConcept.Core.Models;

/// <summary>
/// The one order of every model picker (the music page, the video page's music models, the step pickers' image, voice and text models): the
/// models that run on this computer first, then the online ones, the red ones (this computer cannot run them) always last; the catalog's order
/// (the order of the list given) is kept inside each group. When nothing is chosen, the first model that is not red is chosen. One place for
/// the rule: before 0.1.60 the music lists had a static helper of their own that put the red local models before the online ones.
/// </summary>
public interface IModelListOrder
{
    /// <summary>The group of <paramref name="model"/>; <paramref name="cannotRun"/> = the picker's own red verdict for this computer.</summary>
    ModelListGroup GroupOf(CatalogModel model, bool cannotRun);

    /// <summary><paramref name="items"/> in the groups' order, the given order kept inside each group (a stable sort).</summary>
    IReadOnlyList<T> Order<T>(IEnumerable<T> items, Func<T, ModelListGroup> group);

    /// <summary>The choice when nothing is chosen: the first of <paramref name="items"/> that is not red, else the first; null for an empty list.</summary>
    T? First<T>(IEnumerable<T> items, Func<T, ModelListGroup> group)
        where T : class;
}
