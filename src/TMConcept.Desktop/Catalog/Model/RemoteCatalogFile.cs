namespace TMConcept.Desktop.Catalog;

/// <summary>
/// The repository's model catalog (<c>catalog/models.json</c>, 0.1.26): models a release adds without an app update. Every field is
/// checked by <see cref="RemoteCatalogReader"/>; a field the app does not know refuses the whole file.
/// </summary>
public sealed class RemoteCatalogFile
{
    /// <summary>The file's format (1); a higher one refuses the whole file (a newer app reads it).</summary>
    public int Schema { get; set; }

    /// <summary>When the file was last changed (<c>yyyy-MM-dd</c>).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>The oldest app version that may use it (<c>0.1.26</c>); an older app ignores the file.</summary>
    public string MinApp { get; set; } = string.Empty;

    /// <summary>Models of the cloud providers the app already knows.</summary>
    public List<RemoteProviderModel>? Providers { get; set; }

    /// <summary>Local image models run by stable-diffusion.cpp.</summary>
    public List<RemoteImageModel>? Images { get; set; }

    /// <summary>Models for the app's own Ollama copy.</summary>
    public List<RemoteOllamaModel>? Ollama { get; set; }
}
