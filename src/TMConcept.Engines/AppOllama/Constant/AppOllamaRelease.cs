using TMConcept.Core.Tools;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// The pinned Ollama release of the app's own copy: v0.40.16 (9 Nov 2026, MIT), the official packages of github.com/ollama/ollama. Sizes and
/// SHA-256 are the GitHub release asset digests (<c>https://api.github.com/repos/ollama/ollama/releases/tags/v0.40.16</c>, read on
/// 17 Nov 2026); the download is checked against them before anything is unpacked. Windows x64: the full zip (<c>ollama.exe</c> at its top,
/// <c>lib/ollama/</c> with the processor libraries and the <c>cuda_v12</c> (≈1.19 GB), <c>cuda_v13</c> (≈0.95 GB) and <c>vulkan</c> (≈42 MB)
/// backends, listed from its central directory); only the backend this computer needs is kept after unpacking
/// (<see cref="AppOllamaBackends"/>). Linux x64 and ARM64: the <c>.tar.zst</c> packages (<c>bin/ollama</c> first, then <c>lib/ollama/</c>), read
/// with a managed zstd decoder. The ROCm, MLX and Jetson packages are not used (Vulkan covers AMD cards). Updating = a new version here with
/// new digests; the version change of the app then offers to install it again.
/// </summary>
public static class AppOllamaRelease
{
    /// <summary>The pinned version.</summary>
    public const string Version = "0.40.16";

    /// <summary>The release download folder.</summary>
    private const string Base = "https://github.com/ollama/ollama/releases/download/v" + Version + "/";

    /// <summary>The pinned packages per runtime id.</summary>
    public static readonly IReadOnlyList<ToolArchive> Archives =
    [
        new(ToolKind.AppOllama, Version, "win-x64", new Uri(Base + "ollama-windows-amd64.zip"),
            "50fcf5ad54277f2fa5d173ebfc93bcb017adbc0c9ef2314daabc96ca9e6aa64e", string.Empty, "ollama.exe", 1_618_244_995),
        new(ToolKind.AppOllama, Version, "linux-x64", new Uri(Base + "ollama-linux-amd64.tar.zst"),
            "87bfafb2acbc89b2e6a1be0a9043de6aa75fb450696f86bc426e65fc448f788d", string.Empty, "bin/ollama", 1_545_767_640),
        new(ToolKind.AppOllama, Version, "linux-arm64", new Uri(Base + "ollama-linux-arm64.tar.zst"),
            "e45b8e6dcb86cb9f3057396351655079c9a4879b4ced2678c9350d375dcbc224", string.Empty, "bin/ollama", 1_600_610_073),
    ];
}
