using TMConcept.Core.Tools;

namespace TMConcept.Engines.Images;

/// <summary>A pinned single-file download from huggingface.co (a weights file, not an archive).</summary>
public static class HuggingFaceFile
{
    /// <summary>
    /// The download of <paramref name="file"/> in the Hugging Face repository <paramref name="repository"/> at the commit
    /// <paramref name="commit"/> (40 hex characters, never a branch: a file uploaded again on <c>main</c> would fail its SHA-256 for good),
    /// with its SHA-256 and size.
    /// </summary>
    public static ToolArchive Pinned(string repository, string commit, string file, string sha256, long bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(repository);
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (commit is not { Length: 40 } || !commit.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("A Hugging Face file is pinned to a commit (40 lower-case hex characters), not to " + commit, nameof(commit));
        }

        var name = file[(file.LastIndexOf('/') + 1)..];
        return new ToolArchive(ToolKind.ImageWeights, "1", "any", new Uri("https://huggingface.co/" + repository + "/resolve/" + commit + "/" + file), sha256,
            string.Empty, name, bytes);
    }
}
