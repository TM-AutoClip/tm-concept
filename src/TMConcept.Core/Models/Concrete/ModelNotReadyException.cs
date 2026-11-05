namespace TMConcept.Core.Models;

/// <summary>A step has no model chosen, the model is unknown, or its provider has no key. The message tells which.</summary>
public sealed class ModelNotReadyException : Exception
{
    /// <summary>Creates.</summary>
    public ModelNotReadyException()
    {
    }

    /// <summary>With a message.</summary>
    public ModelNotReadyException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and an inner error.</summary>
    public ModelNotReadyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The step.</summary>
    public string Step { get; init; } = string.Empty;

    /// <summary>What is missing: <c>model</c>, <c>unknown</c> or <c>key</c>.</summary>
    public string Missing { get; init; } = string.Empty;
}
