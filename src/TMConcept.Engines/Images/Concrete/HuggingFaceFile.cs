using TMConcept.Core.Tools;

namespace TMConcept.Engines.Images;

/// <summary>A pinned single-file download from huggingface.co (a weights file, not an archive).</summary>
public static class HuggingFaceFile
{
    /// <summary>The download of <c>https://huggingface.co/&lt;path&gt;</c> with its SHA-256 and size.</summary>
    public static ToolArchive Pinned(string path, string sha256, long bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var name = path[(path.LastIndexOf('/') + 1)..];
        return new ToolArchive(ToolKind.ImageWeights, "1", "any", new Uri("https://huggingface.co/" + path), sha256, string.Empty, name, bytes);
    }
}
