namespace TMConcept.Tests.Storage;

/// <summary>A synchronization context that stands for the window's thread (work posted to it goes to the thread pool, as the base does).</summary>
internal sealed class MarkedContext : SynchronizationContext
{
    public override SynchronizationContext CreateCopy() => this;
}
