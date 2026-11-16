namespace TMConcept.Core.Hardware;

/// <summary>
/// What the NVIDIA CUDA driver says of itself: the CUDA version it supports (<c>cuDriverGetVersion</c>: 12080 = CUDA 12.8, which needs an
/// NVIDIA driver of 570 or newer) and the highest compute capability of its cards (major × 10 + minor: 61 = Pascal GTX 10, 75 = Turing GTX 16
/// and RTX 20, 120 = RTX 50). 0 = not known (no driver, or it could not be asked): an unknown figure is never held against a card.
/// </summary>
public sealed record CudaDetails(int DriverVersion, int ComputeCapability)
{
    /// <summary>Nothing known.</summary>
    public static CudaDetails Unknown { get; } = new(0, 0);
}
