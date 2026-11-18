namespace TMConcept.Desktop.Catalog;

/// <summary>
/// Where the remote model catalog comes from and what it may hold (0.1.26, the developer: "a release that only adds models needs no app
/// update"). Only the project's own repository over HTTPS, read at most once a day in the background, never at the cost of the start.
/// </summary>
public static class RemoteCatalogRules
{
    /// <summary>The file in the public repository (the only address read; no redirect to another host is followed).</summary>
    public static readonly Uri RepositoryFile = new("https://raw.githubusercontent.com/TM-AutoClip/tm-concept/main/catalog/models.json");

    /// <summary>The format this app reads.</summary>
    public const int Schema = 1;

    /// <summary>Least time between two reads of the repository file.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>The longest wait for the file.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The largest file accepted (bytes).</summary>
    public const int MaxFileBytes = 1 << 20;

    /// <summary>The most entries of one kind.</summary>
    public const int MaxEntries = 300;

    /// <summary>The only engine of a remote image model (stable-diffusion.cpp).</summary>
    public const string ImageEngine = "sd-cpp";

    /// <summary>
    /// The only host image weights may come from (also in the download allow-list, <c>ToolManifest.AllowedHosts</c>): files are named by
    /// repository, commit and path, which only Hugging Face's address form gives.
    /// </summary>
    public const string ModelHost = "huggingface.co";

    /// <summary>
    /// The newest stable-diffusion.cpp build every published app can run (the lower of the pinned sd-cli builds: Windows 1106, Linux 1043); a
    /// model that needs a newer one is left out until an app update pins it.
    /// </summary>
    public const int MaxSdCppBuild = 1043;

    /// <summary>Where an Ollama model's library page must be.</summary>
    public const string OllamaLibrary = "https://ollama.com/library/";

    /// <summary>The largest weights file or model download accepted (bytes).</summary>
    public const long MaxModelBytes = 200L << 30;
}
