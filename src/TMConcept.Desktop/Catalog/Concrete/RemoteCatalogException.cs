namespace TMConcept.Desktop.Catalog;

/// <summary>The remote model catalog was refused as a whole (<see cref="Exception.Message"/> says why; logged in one line, the built-in catalog stays).</summary>
public sealed class RemoteCatalogException : Exception
{
    /// <summary>Creates.</summary>
    public RemoteCatalogException()
    {
    }

    /// <summary>Creates.</summary>
    public RemoteCatalogException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public RemoteCatalogException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
