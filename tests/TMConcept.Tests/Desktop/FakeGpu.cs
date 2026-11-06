using TMConcept.Core.Hardware;

namespace TMConcept.Tests.Desktop;

/// <summary>A graphics card of a fixed size (no process is started).</summary>
internal sealed class FakeGpu(long? bytes) : IGpuInfo
{
    public Task<long?> VideoMemoryBytesAsync(CancellationToken cancellationToken) => Task.FromResult(bytes);
}
