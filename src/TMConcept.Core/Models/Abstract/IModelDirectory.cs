namespace TMConcept.Core.Models;

/// <summary>
/// The <see cref="IModel"/> of every catalog entry: a local one from the first <see cref="ILocalModelSource"/> that knows it, otherwise an
/// <see cref="OnlineModel"/> (an unknown <c>local</c> entry: a <see cref="ProcessorModel"/>). The same entry gives the same object for the run
/// (so its loaded state is kept).
/// </summary>
public interface IModelDirectory
{
    /// <summary>The model of <paramref name="model"/>.</summary>
    IModel Get(CatalogModel model);
}