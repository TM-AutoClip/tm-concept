using TMConcept.Core.Tools;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The pinned files of the local OCR on the graphics card (0.1.27, Windows x64): the DirectML build of ONNX Runtime 1.24.4 (MIT; NuGet
/// <c>Microsoft.ML.OnnxRuntime.DirectML</c>, the last DirectML build, 17 Mar 2026) and DirectML 1.15.4 (NuGet <c>Microsoft.AI.DirectML</c>, under
/// Microsoft's own DirectML licence terms: it may be redistributed with an app, but it is not open source, so it is never bundled and comes only
/// after the user agreed), with their licence texts. Each file is one entry of its package, fetched with a range request (about 15 MB instead of
/// the 214 MB of both packages) and checked by the SHA-256 of the unpacked file (checked on 18 Nov 2026). The app keeps its bundled processor
/// build of ONNX Runtime (1.31.1) for everything else; this copy is loaded by its full path and used only through its C function table.
/// </summary>
public static class DirectMlRuntimeRelease
{
    /// <summary>The ONNX Runtime version of the DirectML build (its folder under <c>&lt;tools&gt;/ocr/directml/</c>).</summary>
    public const string Version = "1.24.4";

    /// <summary>The DirectML version.</summary>
    public const string DirectMlVersion = "1.15.4";

    /// <summary>The display name.</summary>
    public const string Name = "ONNX Runtime DirectML + DirectML (text recognition on the graphics card)";

    /// <summary>The ONNX Runtime library's file name.</summary>
    public const string RuntimeFile = "onnxruntime.dll";

    /// <summary>The DirectML library's file name.</summary>
    public const string DirectMlFile = "DirectML.dll";

    private const string RuntimePackage =
        "https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime.directml/1.24.4/microsoft.ml.onnxruntime.directml.1.24.4.nupkg";

    private const string DirectMlPackage = "https://api.nuget.org/v3-flatcontainer/microsoft.ai.directml/1.15.4/microsoft.ai.directml.1.15.4.nupkg";

    /// <summary>ONNX Runtime with the DirectML backend built in (<c>runtimes/win-x64/native/onnxruntime.dll</c>).</summary>
    public static readonly ZipEntryArchive Runtime = new(ToolKind.OcrGpuRuntime, Version, new Uri(RuntimePackage), 6_342_542, 6_039_111, true,
        "e7eedec6a6f26dc39dc948276a75ef6d2bee3fff944d874ceed0bbd3b97bff40", RuntimeFile, 17_328_152); // SHA-256 of the unpacked file

    /// <summary>ONNX Runtime's licence (MIT).</summary>
    public static readonly ZipEntryArchive RuntimeLicense = new(ToolKind.OcrGpuRuntime, Version, new Uri(RuntimePackage), 167_666, 637, true,
        "c250d6278f0b47a6439fb7592b08b58a55eb9f535aa49a1db63211c3f982b674", "LICENSE-onnxruntime.txt", 1_094); // SHA-256 of the unpacked file

    /// <summary>ONNX Runtime's third-party notices.</summary>
    public static readonly ZipEntryArchive RuntimeNotices = new(ToolKind.OcrGpuRuntime, Version, new Uri(RuntimePackage), 12_394_929, 31_400, true,
        "fb0af774b4d7cffc5b9d046f2aaeade2f37df2f80abf8033c95dfffcc77a8866", "ThirdPartyNotices-onnxruntime.txt", 331_175); // SHA-256 of the unpacked file

    /// <summary>DirectML (<c>bin/x64-win/DirectML.dll</c>).</summary>
    public static readonly ZipEntryArchive DirectMl = new(ToolKind.OcrGpuRuntime, DirectMlVersion, new Uri(DirectMlPackage), 142_873_434, 9_332_741, true,
        "9c9e6d822561c6c41b90e6994b3e8857cf1d66dbfb1e0c4c799c7c89b4e92da1", DirectMlFile, 18_527_776); // SHA-256 of the unpacked file

    /// <summary>DirectML's licence terms (Microsoft).</summary>
    public static readonly ZipEntryArchive DirectMlLicense = new(ToolKind.OcrGpuRuntime, DirectMlVersion, new Uri(DirectMlPackage), 105_183_262, 4_246, true,
        "a05138e3a085ff60a44881eedfa58dccb03ecc1d7b1f6ae888418e8c2fec4b8d", "LICENSE-DirectML.txt", 10_439); // SHA-256 of the unpacked file

    /// <summary>Every file, the licences first (so a folder never holds a library without its licence).</summary>
    public static readonly IReadOnlyList<ZipEntryArchive> Files = [RuntimeLicense, RuntimeNotices, DirectMlLicense, DirectMl, Runtime];
}
