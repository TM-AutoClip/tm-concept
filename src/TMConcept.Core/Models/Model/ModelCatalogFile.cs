namespace TMConcept.Core.Models;

/// <summary>An embedded catalog file of one provider.</summary>
public sealed class ModelCatalogFile
{
    /// <summary>Provider code.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Date of the price table (ISO).</summary>
    public string PricedOn { get; set; } = string.Empty;

    /// <summary>Models.</summary>
    public List<CatalogModel> Models { get; set; } = [];
}
