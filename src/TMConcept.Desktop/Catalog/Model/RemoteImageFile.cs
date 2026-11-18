namespace TMConcept.Desktop.Catalog;

/// <summary>One weights file of a remote image model: where it is (an allowed model host, a repository at a commit, a path) and what it must be.</summary>
public sealed class RemoteImageFile
{
    /// <summary>What it is (<c>Diffusion</c>, <c>Full</c>, <c>Llm</c>, <c>ClipL</c>, <c>ClipG</c>, <c>Vae</c>).</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The host it is downloaded from (only <c>huggingface.co</c>).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The repository (<c>owner/name</c>).</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>The commit (40 lower-case hex characters, never a branch).</summary>
    public string Commit { get; set; } = string.Empty;

    /// <summary>The file's path in the repository.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Its SHA-256 (64 lower-case hex characters).</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Its size in bytes.</summary>
    public long Bytes { get; set; }
}
