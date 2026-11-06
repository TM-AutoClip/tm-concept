namespace TMConcept.Core.Models;

/// <summary>Provider codes that are not online providers.</summary>
public static class ModelProviders
{
    /// <summary>Models that run on this computer (local voices, image models, embedding): free, no key, installed when first used.</summary>
    public const string Local = "local";

    /// <summary>An optional step turned off explicitly.</summary>
    public const string Off = "off";
}
