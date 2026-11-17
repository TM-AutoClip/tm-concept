namespace TMConcept.Core.AppOllama;

/// <summary>A model in the app's own Ollama models folder, read from its manifest (<see cref="AppModelStore"/>).</summary>
/// <param name="Tag">The full tag (<c>qwen3.5:4b-q4_K_M</c>).</param>
/// <param name="Bytes">Bytes of its layers and config (a layer shared with another model counts for both).</param>
/// <param name="Digests">The blobs it uses (<c>sha256:…</c>, the config included).</param>
/// <param name="Complete">Whether every blob is on disk with its size (a blob removed by hand makes the model unusable).</param>
public sealed record InstalledAppModel(string Tag, long Bytes, IReadOnlyList<string> Digests, bool Complete);
