namespace TMConcept.Desktop.Catalog;

/// <summary>
/// A model for the app's own Ollama copy: an exact tag of the Ollama library with the facts the built-in ones carry, and the digest of its
/// manifest (Ollama checks every layer's SHA-256 itself; the app checks that the manifest it got is the one this file names).
/// </summary>
public sealed class RemoteOllamaModel
{
    /// <summary>The exact tag (<c>qwen3.5:9b-q4_K_M</c>; never <c>latest</c>).</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The family (<c>qwen3.5</c>).</summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>Bytes of the download (every layer of the manifest).</summary>
    public long Bytes { get; set; }

    /// <summary>The context the app loads it with.</summary>
    public long ContextTokens { get; set; }

    /// <summary>The longest context it was trained with.</summary>
    public long TrainedContextTokens { get; set; }

    /// <summary>Graphics memory one token of context takes (estimate).</summary>
    public long KvBytesPerToken { get; set; }

    /// <summary>It reads images.</summary>
    public bool Vision { get; set; }

    /// <summary>It can think before it answers.</summary>
    public bool CanThink { get; set; }

    /// <summary>License code.</summary>
    public string License { get; set; } = string.Empty;

    /// <summary>What the license allows (<c>Commercial</c>, <c>Limited</c>, <c>NonCommercial</c>).</summary>
    public string LicenseUse { get; set; } = string.Empty;

    /// <summary>Where the license is (https; required unless the use is <c>Commercial</c>).</summary>
    public string LicenseUrl { get; set; } = string.Empty;

    /// <summary>The Ollama library page of the tag (<c>https://ollama.com/library/…</c>).</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>The digest Ollama lists for the tag after its pull (<c>sha256:</c> + the SHA-256 of its manifest).</summary>
    public string ManifestDigest { get; set; } = string.Empty;
}
