namespace TMConcept.Core.Models;

/// <summary>A model of an online provider, or of a user-added server on another computer: it never takes this computer's graphics card.</summary>
public sealed class OnlineModel(CatalogModel catalog, bool needsKey) : ModelBase(catalog), IOnlineModel
{
    /// <inheritdoc />
    public override bool IsLocal => false;

    /// <inheritdoc />
    public bool NeedsKey { get; } = needsKey;
}