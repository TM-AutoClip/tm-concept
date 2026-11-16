namespace TMConcept.Core.Endpoints;

/// <summary>
/// A model a server holds in memory now: <paramref name="TotalBytes"/> in all (weights, context cache, buffers), of which
/// <paramref name="VideoMemoryBytes"/> on the graphics card (Ollama's <c>size</c> and <c>size_vram</c>; 0 and 0 when the server does not say,
/// as LM Studio), and the context it was loaded with (0 = not said).
/// </summary>
public sealed record LoadedServerModel(string Id, long TotalBytes, long VideoMemoryBytes, long ContextTokens)
{
    /// <summary>The part in main memory (what did not fit on the card: slower).</summary>
    public long MainMemoryBytes => Math.Max(0, TotalBytes - VideoMemoryBytes);

    /// <summary>Whether the server reported the memory figures.</summary>
    public bool HasMemory => TotalBytes > 0;
}
