namespace TMConcept.Core.Hardware;

/// <summary>
/// The computer's main memory (RAM) free now, read again at every call: a local image model whose weights do not fit on the graphics card
/// keeps them in main memory instead, which needs about their size free. Never throws.
/// </summary>
public interface ISystemMemory
{
    /// <summary>Main memory a new program can use now, in bytes (Windows: the available physical memory; Linux: MemAvailable); null when unknown.</summary>
    long? AvailableBytes();
}
