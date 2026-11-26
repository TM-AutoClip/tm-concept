namespace TMConcept.Core.Tools;

/// <summary>Programs the app downloads into its own tools folder after the user agrees (the system PATH is never changed).</summary>
public enum ToolKind
{
    /// <summary>Node.js (runs the video engines).</summary>
    Node = 0,

    /// <summary>ffmpeg + ffprobe (mux, subtitles, thumbnails).</summary>
    Ffmpeg = 1,

    /// <summary>Supertonic 3 voice model (sherpa-onnx).</summary>
    SupertonicVoice = 2,

    /// <summary>sherpa-onnx with ONNX Runtime: the native libraries the speech worker loads for the local voice (not in the app itself).</summary>
    SpeechRuntime = 3,

    /// <summary>
    /// stable-diffusion.cpp (sd-cli), the CUDA build (NVIDIA graphics cards, Windows; with <see cref="ImageRuntimeCuda"/>). The Vulkan and
    /// processor builds are <see cref="ImageRuntimeVulkan"/> and <see cref="ImageRuntimeCpu"/>.
    /// </summary>
    ImageRuntime = 4,

    /// <summary>The CUDA runtime libraries next to the Windows CUDA build of stable-diffusion.cpp (NVIDIA graphics cards).</summary>
    ImageRuntimeCuda = 5,

    /// <summary>A weights file of a local image model (one file, not an archive).</summary>
    ImageWeights = 6,

    /// <summary>Kokoro 82M voice model (sherpa-onnx, English).</summary>
    KokoroVoice = 7,

    /// <summary>A file of the local embedding model (one file, not an archive; run with the ONNX Runtime of the speech libraries).</summary>
    EmbeddingWeights = 8,

    /// <summary>Pocket TTS voice model (sherpa-onnx, English, copies a voice from a sample).</summary>
    PocketVoice = 9,

    /// <summary>A file of the Chatterbox Multilingual V3 voice model (ONNX graphs, run with the ONNX Runtime of the speech libraries).</summary>
    ChatterboxVoice = 10,

    /// <summary>stable-diffusion.cpp (sd-cli), the Vulkan build (AMD, Intel and NVIDIA graphics cards; needs only the graphics driver).</summary>
    ImageRuntimeVulkan = 11,

    /// <summary>stable-diffusion.cpp (sd-cli), the processor build (no graphics card).</summary>
    ImageRuntimeCpu = 12,

    /// <summary>
    /// uv (Astral, MIT/Apache-2.0): installs the pinned Python and the packages of the Python voices into the tools folder (never the system's
    /// copy, never the system PATH).
    /// </summary>
    Uv = 13,

    /// <summary>A file of a Python voice model (one file from Hugging Face, pinned by commit and SHA-256; not an archive).</summary>
    PythonVoiceWeights = 14,

    /// <summary>A file of the Ahenk Cep voice model (Turkish, ONNX graphs, run with the ONNX Runtime of the speech libraries).</summary>
    AhenkCepVoice = 15,

    /// <summary>
    /// The app's own Ollama copy (the official release package of github.com/ollama/ollama; only the graphics backend this computer needs is
    /// kept after unpacking). Runs only as the app's own server, never as the user's Ollama.
    /// </summary>
    AppOllama = 16,

    /// <summary>A model file of the local OCR engine of scanned pages (0.1.25; one ONNX file, run in the app with its bundled ONNX Runtime).</summary>
    OcrWeights = 17,

    /// <summary>
    /// PDFium (0.1.27; the pdfium-binaries build, BSD-3-Clause with permissive parts): draws any PDF page as a picture for the OCR or a vision
    /// model (JPEG 2000, JBIG2 and CCITT fax scans, text drawn as shapes), loaded by the app from the tools folder.
    /// </summary>
    PdfRenderer = 18,

    /// <summary>
    /// The DirectML build of ONNX Runtime with Microsoft's DirectML library (0.1.27, Windows): the local OCR on any graphics card. Two files of
    /// two NuGet packages, fetched entry by entry; DirectML is under Microsoft's own licence terms (downloaded after the user agreed).
    /// </summary>
    OcrGpuRuntime = 19,

    /// <summary>A track of the ready music library (0.1.45; one file from incompetech.com, pinned by its SHA-256; not an archive).</summary>
    MusicTrack = 20,

    /// <summary>A source archive of the local music model (0.1.46: rakkase and ACE-Step 1.5 at pinned commits, from GitHub; not a program).</summary>
    MusicCode = 21,

    /// <summary>One weight file of the local music model (0.1.46: ACE-Step 1.5 and Qwen3-Embedding from Hugging Face, pinned by SHA-256).</summary>
    MusicWeights = 22,

    /// <summary>
    /// whisper.cpp (0.1.48, MIT, ggml-org): whisper-cli of the official processor build, which hears the narration and gives each word its
    /// time (S3); only the subtitles' timing uses it, never the text.
    /// </summary>
    SpeechRecognizer = 23,

    /// <summary>The multilingual ggml model file of whisper.cpp (0.1.48; one file from Hugging Face, pinned by commit and SHA-256).</summary>
    RecognizerWeights = 24,

    /// <summary>
    /// yt-dlp (0.1.52, Unlicense): the official single-file program of its GitHub release, which downloads one reference video (S1: no
    /// playlist, no cookies or sign-in, at most 480p, the first minutes only).
    /// </summary>
    VideoDownloader = 25,

    /// <summary>
    /// The SoundFont of the composed music (0.1.55, MuseScore General 0.2, MIT; one file from the MuseScore mirror at ftp.osuosl.org, pinned by
    /// its SHA-256; not an archive).
    /// </summary>
    SoundFont = 26,

    /// <summary>A new version of the app itself (a release asset; its SHA-256 comes from the release notes). Not a component.</summary>
    AppUpdate = 100,
}
