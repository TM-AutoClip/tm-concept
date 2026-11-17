namespace TMConcept.Core.DiskSpace;

/// <summary>A group of storage rows (the components, the leftovers and caches; later documents and the app's Ollama models), registered in DI.</summary>
public interface IDiskItemSource
{
    /// <summary>The rows now, with <paramref name="usage"/> deciding which components are used.</summary>
    Task<IReadOnlyList<IDiskItem>> ListAsync(UsageMap usage, CancellationToken cancellationToken);
}
