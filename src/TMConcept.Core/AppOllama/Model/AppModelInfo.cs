using TMConcept.Core.Models;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// A model the app recommends for its own Ollama copy (<see cref="AppModelCatalog"/>): an exact tag of the Ollama library, its size as the
/// registry lists it, the context the app runs it with and what it can do. The tag and its size were read from the library page named in
/// <see cref="SourceUrl"/> and from the registry manifest of that tag.
/// </summary>
/// <param name="Tag">The exact Ollama tag that is pulled (<c>qwen3.5:4b-q4_K_M</c>; never a floating one such as <c>latest</c>).</param>
/// <param name="Name">Display name ("Qwen 3.5 4B").</param>
/// <param name="Family">The model family (<c>qwen3.5</c>, <c>gemma4</c>, <c>gpt-oss</c>).</param>
/// <param name="Bytes">Bytes of the download (every layer of the tag's manifest).</param>
/// <param name="ContextTokens">The context the app loads it with (<c>num_ctx</c>): what fits its card class next to the weights.</param>
/// <param name="TrainedContextTokens">The longest context the model was trained with (an upper limit for <see cref="ContextTokens"/>).</param>
/// <param name="KvBytesPerToken">About how many bytes of graphics memory one token of context takes (the KV cache at f16; an estimate).</param>
/// <param name="Vision">It reads images.</param>
/// <param name="CanThink">It can think before it answers (Ollama's <c>think</c>; the app sends it off by default).</param>
/// <param name="License">The license code (<c>apache-2.0</c>), named in the picker by <c>license.{code}</c>.</param>
/// <param name="LicenseUse">What the license allows.</param>
/// <param name="LicenseUrl">Where the license is.</param>
/// <param name="SourceUrl">The Ollama library page the tag and its size were read from.</param>
public sealed record AppModelInfo(string Tag, string Name, string Family, long Bytes, long ContextTokens, long TrainedContextTokens, long KvBytesPerToken,
    bool Vision, bool CanThink, string License, LicenseUse LicenseUse, string LicenseUrl, string SourceUrl);
