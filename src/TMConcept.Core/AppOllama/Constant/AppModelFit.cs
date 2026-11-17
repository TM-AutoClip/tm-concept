namespace TMConcept.Core.AppOllama;

/// <summary>Whether a recommended model fits this computer's graphics card with its context (<see cref="AppModelAdvisor"/>).</summary>
public enum AppModelFit
{
    /// <summary>Weights and context fit on the card (green).</summary>
    Fits = 0,

    /// <summary>It does not fit on the card: Ollama puts the rest in main memory, which is much slower (red).</summary>
    TooLarge = 1,

    /// <summary>No usable graphics card (or its memory is unknown): it runs on the processor, slowly (red).</summary>
    Processor = 2,
}
