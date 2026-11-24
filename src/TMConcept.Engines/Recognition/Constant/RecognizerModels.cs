using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Recognition;

/// <summary>
/// The whisper.cpp model of the subtitles' word times (0.1.48, S3): the multilingual base model ggml-base.bin (MIT, OpenAI's Whisper
/// weights converted by the whisper.cpp project) from huggingface.co/ggerganov/whisper.cpp, pinned to a commit; SHA-256 and size are the
/// repository's LFS record read on 2026-11-24. Base is the default (about 148 MB, enough for word times of a known text); the multilingual
/// small model (ggml-small.bin, about 488 MB, SHA-256 1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b) would be a second
/// row here if base proves too weak.
/// </summary>
public static class RecognizerModels
{
    /// <summary>The Hugging Face repository.</summary>
    public const string Repository = "ggerganov/whisper.cpp";

    /// <summary>The pinned commit of the repository.</summary>
    public const string Commit = "5359861c739e955e79d9a303bcbc70fb988958b1";

    /// <summary>The model's name in its component code (<see cref="ComponentCodes.RecognizerModel"/>).</summary>
    public const string Name = "base";

    /// <summary>The model file.</summary>
    public static readonly ToolArchive File = new(ToolKind.RecognizerWeights, "1", "any",
        new Uri($"https://huggingface.co/{Repository}/resolve/{Commit}/ggml-base.bin"),
        "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", string.Empty, "ggml-base.bin", 147_951_465);
}
