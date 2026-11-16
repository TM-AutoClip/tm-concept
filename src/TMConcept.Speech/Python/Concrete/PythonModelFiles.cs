using TMConcept.Core.Tools;

namespace TMConcept.Speech.Python;

/// <summary>Pinned Hugging Face files of the Python voices.</summary>
public static class PythonModelFiles
{
    /// <summary>
    /// The download of <paramref name="file"/> in the Hugging Face repository <paramref name="repository"/> at the commit
    /// <paramref name="commit"/> (40 lower-case hex characters, never a branch), with its SHA-256 and size; it is saved as
    /// <paramref name="target"/> in the model folder (default: the file's own path).
    /// </summary>
    public static ToolArchive Pinned(string repository, string commit, string file, string sha256, long bytes, string? target = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(repository);
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (commit is not { Length: 40 } || !commit.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("A Hugging Face file is pinned to a commit (40 lower-case hex characters), not to " + commit, nameof(commit));
        }

        if (sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("A pinned file needs its SHA-256 (64 lower-case hex characters): " + file, nameof(sha256));
        }

        return new ToolArchive(ToolKind.PythonVoiceWeights, commit[..7], "any", new Uri("https://huggingface.co/" + repository + "/resolve/" + commit + "/" + file),
            sha256, string.Empty, target ?? file, bytes);
    }

    /// <summary>The files of a VoxCPM2 model folder (the tokenizer, configuration and audio VAE are the same in both repositories).</summary>
    public static IReadOnlyList<ToolArchive> VoxCpm(string repository, string commit, string modelSha256, long modelBytes) =>
    [
        Pinned(repository, commit, "config.json", "405f0dcd92f7feba6011ed4eac5c8d4f74cba9712f07fd5cfa3063bbdd95402c", 4_336),
        Pinned(repository, commit, "special_tokens_map.json", "068594063e37662c02b21acf42ebb334ef6a74fb810e68a2368f88f08351de76", 1_632),
        Pinned(repository, commit, "tokenization_voxcpm2.py", "84489ea32b6ee0cae22ed5480cacb6df85c46624c3119be9a2021c3649a12729", 2_895),
        Pinned(repository, commit, "tokenizer.json", "f8984687e4a92a3503d521396d454b7d68e9fdaab2a0288eb3536c7c1aa4bc20", 3_676_772),
        Pinned(repository, commit, "tokenizer_config.json", "e78a3ebb48a0b9437efd1823b6b726c823da89e49dd8bcc90c02419d9baa772b", 5_059),
        Pinned(repository, commit, "audiovae.pth", "94b5d51e107e0507d4acc976cfdadb64edd6fd06d1f751dadbf2fd1594274bf1", 376_951_122),
        Pinned(repository, commit, "model.safetensors", modelSha256, modelBytes),
    ];
}
