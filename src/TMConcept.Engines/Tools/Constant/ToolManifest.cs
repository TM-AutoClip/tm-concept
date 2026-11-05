using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Pinned tool downloads. Node: the official nodejs.org archives of LTS 26.13.0 (hashes from its SHASUMS256.txt). ffmpeg: a dated BtbN
/// FFmpeg-Builds release (GPL build, a separate program; never the floating "latest"); hashes from that release's checksums. Updating a
/// version = new rows here with new hashes.
/// </summary>
public static class ToolManifest
{
    /// <summary>Node version.</summary>
    public const string NodeVersion = "26.13.0";

    /// <summary>ffmpeg release tag.</summary>
    public const string FfmpegTag = "autobuild-2026-11-04-12-51";

    /// <summary>ffmpeg build name inside the release.</summary>
    public const string FfmpegBuild = "ffmpeg-n8.1.3-34-ga35157abb9";

    /// <summary>Hosts a download may come from (redirect targets included).</summary>
    public static readonly IReadOnlyList<string> AllowedHosts = ["nodejs.org", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    /// <summary>Every pinned archive.</summary>
    public static readonly IReadOnlyList<ToolArchive> Archives =
    [
        new(ToolKind.Node, NodeVersion, "win-x64", new Uri($"https://nodejs.org/dist/v{NodeVersion}/node-v{NodeVersion}-win-x64.zip"),
            "dcc60d59a0746999f99064a01c020445d54d84228c691cdbf25b49ac3b59150a", $"node-v{NodeVersion}-win-x64", "node.exe", 35_000_000),
        new(ToolKind.Node, NodeVersion, "linux-x64", new Uri($"https://nodejs.org/dist/v{NodeVersion}/node-v{NodeVersion}-linux-x64.tar.xz"),
            "d28a96af6c38596f6d9d97c0ee531c13d238d757742387d1928de822d959e997", $"node-v{NodeVersion}-linux-x64", "bin/node", 30_000_000),
        new(ToolKind.Node, NodeVersion, "linux-arm64", new Uri($"https://nodejs.org/dist/v{NodeVersion}/node-v{NodeVersion}-linux-arm64.tar.xz"),
            "406fe3fbde1c054adff8b297c16fdb5af6659db327c1ac8acd46b56ef1af9a92", $"node-v{NodeVersion}-linux-arm64", "bin/node", 30_000_000),
        new(ToolKind.Ffmpeg, "8.1.3", "win-x64", new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/{FfmpegTag}/{FfmpegBuild}-win64-gpl-8.1.zip"),
            "a0c6b648d42427b348ab2c5ad50381463dde8e756a3723371c9b78ecb852d763", $"{FfmpegBuild}-win64-gpl-8.1", "bin/ffmpeg.exe", 200_000_000),
        new(ToolKind.Ffmpeg, "8.1.3", "linux-x64", new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/{FfmpegTag}/{FfmpegBuild}-linux64-gpl-8.1.tar.xz"),
            "0fc51331600b02730e6d63485ac7e3cd203bdeeb13216cd78c8844aeb9277fb8", $"{FfmpegBuild}-linux64-gpl-8.1", "bin/ffmpeg", 130_000_000),
        new(ToolKind.Ffmpeg, "8.1.3", "linux-arm64", new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/{FfmpegTag}/{FfmpegBuild}-linuxarm64-gpl-8.1.tar.xz"),
            "e4d93d4b858a341cf29cbcd0ce2e31271bba67018b27ff4ef3c2e1683f76b810", $"{FfmpegBuild}-linuxarm64-gpl-8.1", "bin/ffmpeg", 110_000_000),
    ];
}
