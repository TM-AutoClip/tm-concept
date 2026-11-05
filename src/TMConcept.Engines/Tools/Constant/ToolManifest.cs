using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Pinned tool downloads. Node: the official nodejs.org archives of LTS 26.13.0 (hashes from its SHASUMS256.txt). ffmpeg: a dated BtbN
/// FFmpeg-Builds release (GPL build, a separate program; never the floating "latest"); hashes from that release's checksums.sha256. BtbN
/// keeps only the last 14 daily builds but the last build of each month for two years, so the pin is always a month-end build (here the
/// last one of October 2026). When the pinned archive is gone or its hash does not match, ffmpeg from the system PATH is used if there is
/// one (<see cref="FallbackToSystem"/>); an unverified download is never run. sherpa-onnx: the official "-lib" archives of the v2.0.1
/// release (only the shared libraries with ONNX Runtime; Windows: the MT build, which needs no Visual C++ runtime), hashes from the
/// release's asset digests; the speech worker loads them, the app itself carries no speech library. Updating a version = new rows here with
/// new hashes.
/// </summary>
public static class ToolManifest
{
    /// <summary>Node version.</summary>
    public const string NodeVersion = "26.13.0";

    /// <summary>ffmpeg release tag (the last build of October 2026: kept by BtbN for two years).</summary>
    public const string FfmpegTag = "autobuild-2026-10-31-12-59";

    /// <summary>ffmpeg build name inside the release.</summary>
    public const string FfmpegBuild = "ffmpeg-n8.1.3-27-g8dca1586f4";

    /// <summary>sherpa-onnx version (must match the C structures in TMConcept.Speech).</summary>
    public const string SherpaVersion = "2.0.1";

    /// <summary>Tools that are libraries, not programs: never looked for on the system PATH.</summary>
    public static readonly IReadOnlyList<ToolKind> Libraries = [ToolKind.SpeechRuntime];

    /// <summary>Tools that may come from the system PATH when their pinned download is gone (404) or does not match its hash.</summary>
    public static readonly IReadOnlyList<ToolKind> FallbackToSystem = [ToolKind.Ffmpeg];

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
            "401dffcf14c301082e1fc03b0bc876b92ea676bfc521fc56e80a1537d137034f", $"{FfmpegBuild}-win64-gpl-8.1", "bin/ffmpeg.exe", 200_000_000),
        new(ToolKind.Ffmpeg, "8.1.3", "linux-x64", new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/{FfmpegTag}/{FfmpegBuild}-linux64-gpl-8.1.tar.xz"),
            "41e9c565594c390622cf968d82fa4135d9e9a11d06c1515644288cbc844e3380", $"{FfmpegBuild}-linux64-gpl-8.1", "bin/ffmpeg", 130_000_000),
        new(ToolKind.Ffmpeg, "8.1.3", "linux-arm64", new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/{FfmpegTag}/{FfmpegBuild}-linuxarm64-gpl-8.1.tar.xz"),
            "a08c0ee6e1c4e02139344b303002dcc7c37cf125d639f1fbd15dabee40fcb525", $"{FfmpegBuild}-linuxarm64-gpl-8.1", "bin/ffmpeg", 110_000_000),
        new(ToolKind.SpeechRuntime, SherpaVersion, "win-x64",
            new Uri($"https://github.com/k2-fsa/sherpa-onnx/releases/download/v{SherpaVersion}/sherpa-onnx-v{SherpaVersion}-win-x64-shared-MT-Release-lib.tar.bz2"),
            "afa4de3ca4ab0afd53bb4aab40d28ee15608364b549ffc0fe0caf6489bfc39a5", $"sherpa-onnx-v{SherpaVersion}-win-x64-shared-MT-Release-lib",
            "lib/sherpa-onnx-c-api.dll", 7_899_712),
        new(ToolKind.SpeechRuntime, SherpaVersion, "linux-x64",
            new Uri($"https://github.com/k2-fsa/sherpa-onnx/releases/download/v{SherpaVersion}/sherpa-onnx-v{SherpaVersion}-linux-x64-shared-lib.tar.bz2"),
            "532c45bbd41036d83cd70606797529aa8086c2053395a58967736c727625b120", $"sherpa-onnx-v{SherpaVersion}-linux-x64-shared-lib",
            "lib/libsherpa-onnx-c-api.so", 9_710_131),
        new(ToolKind.SpeechRuntime, SherpaVersion, "linux-arm64",
            new Uri($"https://github.com/k2-fsa/sherpa-onnx/releases/download/v{SherpaVersion}/sherpa-onnx-v{SherpaVersion}-linux-aarch64-shared-cpu-lib.tar.bz2"),
            "15bd737eae467da32ec97e3740a329a535fd21e33caca4f327b0081451d11533", $"sherpa-onnx-v{SherpaVersion}-linux-aarch64-shared-cpu-lib",
            "lib/libsherpa-onnx-c-api.so", 12_500_491),
    ];
}
