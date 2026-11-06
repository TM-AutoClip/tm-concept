namespace TMConcept.Core.Models;

/// <summary>Provider codes that are not online providers.</summary>
public static class ModelProviders
{
    /// <summary>Models that run on this computer (local voice, local image models): free, no key, installed on save.</summary>
    public const string Local = "local";

    /// <summary>A step turned off explicitly (a profile override of an optional step).</summary>
    public const string Off = "off";
}
