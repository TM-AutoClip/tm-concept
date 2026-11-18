using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Pinned tool downloads. Node: the official nodejs.org archives of LTS 26.13.0 (hashes from its SHASUMS256.txt). ffmpeg: a dated BtbN
/// FFmpeg-Builds release (GPL build, a separate program; never the floating "latest"); hashes from that release's checksums.sha256. BtbN
/// keeps only the last 14 daily builds but the last build of each month for two years, so the pin is always a month-end build (here the
/// last one of October 2026). When the pinned archive is gone or its hash does not match, ffmpeg from the system PATH is used if there is
/// one (<see cref="FallbackToSystem"/>); an unverified download is never run. sherpa-onnx: the official "-lib" archives of the v2.0.1
/// release (only the shared libraries with ONNX Runtime; Windows: the MT build, which needs no Visual C++ runtime), hashes from the
/// release's asset digests; the speech worker loads them, the app itself carries no speech library. stable-diffusion.cpp (MIT, leejet;
/// 0.1.17: a release that knows FLUX.3 Image Klein, build 968 or newer): on Windows master-1106-532d2c1 (the newest on 2026-11-16), on
/// Linux master-1043-3be2378, the last release built on Ubuntu 24.04 (from master-1044 on the Linux zips are built on Ubuntu 26.04 and need
/// glibc 2.43, which 24.04 does not have; 1043 needs at most 2.38). Both take the same options (<c>--flash-attn</c>, <c>--max-vram</c>,
/// <c>--offload-to-cpu</c>, <c>--backend</c>; checked in examples/common/common.cpp at both tags). Hashes and sizes are the GitHub release
/// asset digests, checked on 2026-11-16 by downloading every processor and Vulkan zip (the CUDA zips' file lists were read from their
/// central directories). One build per image backend, each in its own tools folder so they can sit side by side: on Windows the CUDA 12
/// build with its CUDA runtime libraries (NVIDIA), the Vulkan build (AMD, Intel, NVIDIA; needs only vulkan-1.dll from the graphics driver)
/// and the processor build (one zip that picks the processor's best variant at run time); on Linux x64 the Vulkan and the processor build.
/// Every zip is flat: sd-cli sits at its top. There is no Linux CUDA build and nothing for Linux ARM64. The ROCm builds are not used: their
/// zips carry no HIP runtime (hipblas, rocblas must come from AMD's SDK on the PATH, otherwise sd-cli ends with 0xC0000135 and prints
/// nothing). uv (Astral, MIT or Apache-2.0): the 0.13.6 release (26 Oct 2026), hashes from the release's asset digests,
/// checked on 2026-11-16 by downloading the Windows zip (flat: uv.exe at its top) and listing the Linux archive (one folder); it installs the
/// Python of the Python voices and their packages into the tools folder, and is never taken from the system. Updating a version = new rows
/// here with new hashes (and the sd-cli options checked again).
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

    /// <summary>stable-diffusion.cpp release of the Windows builds.</summary>
    public const string SdCppTag = "master-1106-532d2c1";

    /// <summary>stable-diffusion.cpp release of the Linux builds (the last one built on Ubuntu 24.04).</summary>
    public const string SdCppLinuxTag = "master-1043-3be2378";

    /// <summary>uv release (installs the Python voices' Python and packages).</summary>
    public const string UvVersion = "0.13.6";

    /// <summary>
    /// The oldest Node.js major version the engines run on: HyperFrames 0.8 asks for Node 22 or newer (Remotion and Revideo for 18), so a
    /// Node.js of at least this version already on the PATH, with npm next to it, is used instead of downloading one.
    /// </summary>
    public const int NodeMinimumMajor = 22;

    /// <summary>Tools that are libraries, not programs: never looked for on the system PATH.</summary>
    public static readonly IReadOnlyList<ToolKind> Libraries = [ToolKind.SpeechRuntime, ToolKind.ImageRuntime, ToolKind.ImageRuntimeCuda, ToolKind.ImageRuntimeVulkan,
        ToolKind.ImageRuntimeCpu, ToolKind.ImageWeights, ToolKind.EmbeddingWeights, ToolKind.Uv, ToolKind.PythonVoiceWeights, ToolKind.OcrWeights];

    /// <summary>Tools that may come from the system PATH when their pinned download is gone (404) or does not match its hash.</summary>
    public static readonly IReadOnlyList<ToolKind> FallbackToSystem = [ToolKind.Ffmpeg];

    /// <summary>Hosts a download may come from (redirect targets included).</summary>
    public static readonly IReadOnlyList<string> AllowedHosts =
    [
        "nodejs.org", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com", "huggingface.co",

        // ModelScope: the RapidOCR models of the local OCR (0.1.25), served directly (no redirect); each file is pinned by its SHA-256.
        "www.modelscope.cn",

        // nuget.org's package files (0.1.27): two files of the DirectML build of ONNX Runtime and of DirectML, fetched entry by entry with
        // a range request (served directly, no redirect); each unpacked file is pinned by its SHA-256.
        "api.nuget.org",
    ];

    /// <summary>Host suffixes a download may be redirected to: Hugging Face's own file servers (us.aws.cdn.hf.co, cas-bridge.xethub.hf.co …).</summary>
    public static readonly IReadOnlyList<string> AllowedHostSuffixes = [".hf.co"];

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
        new(ToolKind.ImageRuntime, SdCppTag, "win-x64",
            new Uri($"https://github.com/leejet/stable-diffusion.cpp/releases/download/{SdCppTag}/sd-master-532d2c1-bin-win-cuda12-x64.zip"),
            "b087219145a382cc1b1e826318289e1f672b9bb6965977734ef0deb85fe2d03a", string.Empty, "sd-cli.exe", 412_365_497),
        new(ToolKind.ImageRuntimeCuda, SdCppTag, "win-x64",
            new Uri($"https://github.com/leejet/stable-diffusion.cpp/releases/download/{SdCppTag}/cudart-sd-bin-win-cu12-x64.zip"),
            "fe20366827d357c00797eebb58244dddab7fd9a348d70090c3871004c320f38d", string.Empty, "cudart64_12.dll", 563_452_046),
        new(ToolKind.ImageRuntimeVulkan, SdCppTag, "win-x64",
            new Uri($"https://github.com/leejet/stable-diffusion.cpp/releases/download/{SdCppTag}/sd-master-532d2c1-bin-win-vulkan-x64.zip"),
            "3e66222c87ca27074e1fd8ce79706eef17a8aaf39949906aa1bee7e060f22ecc", string.Empty, "sd-cli.exe", 30_312_081),
        new(ToolKind.ImageRuntimeCpu, SdCppTag, "win-x64",
            new Uri($"https://github.com/leejet/stable-diffusion.cpp/releases/download/{SdCppTag}/sd-master-532d2c1-bin-win-cpu-x64.zip"),
            "abef332cfca7861cc567910e79bedb70bfb2920bb4eedac97fcbb6db477f1596", string.Empty, "sd-cli.exe", 18_358_541),
        new(ToolKind.ImageRuntimeVulkan, SdCppLinuxTag, "linux-x64",
            new Uri($"https://github.com/leejet/stable-diffusion.cpp/releases/download/{SdCppLinuxTag}/sd-master-3be2378-bin-Linux-Ubuntu-24.04-x86_64-vulkan.zip"),
            "7608901016fb4a348bc5b75c46fc971085dadadf44e6f30daa97301844b1ce2e", string.Empty, "sd-cli", 37_675_781),
        new(ToolKind.ImageRuntimeCpu, SdCppLinuxTag, "linux-x64",
            new Uri($"https://github.com/leejet/stable-diffusion.cpp/releases/download/{SdCppLinuxTag}/sd-master-3be2378-bin-Linux-Ubuntu-24.04-x86_64.zip"),
            "01e1c773c1bc2a6276952f6dbd13294e58b7255c659b4a770086934c4e8fad3a", string.Empty, "sd-cli", 27_322_680),
        new(ToolKind.Uv, UvVersion, "win-x64", new Uri($"https://github.com/astral-sh/uv/releases/download/{UvVersion}/uv-x86_64-pc-windows-msvc.zip"),
            "a2a8820d5eea32f08ef467ef3b7f66883a9b076d5737a2fc2fbb5f458d1bb50f", string.Empty, "uv.exe", 15_239_500),
        new(ToolKind.Uv, UvVersion, "linux-x64", new Uri($"https://github.com/astral-sh/uv/releases/download/{UvVersion}/uv-x86_64-unknown-linux-gnu.tar.gz"),
            "f29c3e94ffabd7e6f3ba521dba169a9b7ec12100bf3257087f0f24f6123f3645", "uv-x86_64-unknown-linux-gnu", "uv", 16_799_024),
        new(ToolKind.Uv, UvVersion, "linux-arm64", new Uri($"https://github.com/astral-sh/uv/releases/download/{UvVersion}/uv-aarch64-unknown-linux-gnu.tar.gz"),
            "229e194255c7958d86d56e8790d969a6f74736592133f0dfd0076f754c642c1d", "uv-aarch64-unknown-linux-gnu", "uv", 15_794_886),
    ];
}
