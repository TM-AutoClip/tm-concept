namespace TMConcept.Core.Models;

/// <summary>A dated set of prices from one source (<see cref="PriceSources"/>): the repository's prices file has this shape too.</summary>
public sealed class PriceTable
{
    /// <summary>Format version of the file (<see cref="CurrentSchema"/>).</summary>
    public int Schema { get; set; } = CurrentSchema;

    /// <summary>Source (<see cref="PriceSources"/>; not part of the file, set when it is read).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Date of the prices (ISO, yyyy-MM-dd).</summary>
    public string PricedOn { get; set; } = string.Empty;

    /// <summary>Prices.</summary>
    public List<ModelPrice> Models { get; set; } = [];

    /// <summary>The format version this app reads.</summary>
    public const int CurrentSchema = 1;
}
