namespace TMConcept.Core.Endpoints;

/// <summary>What an <see cref="IEndpointModelControl"/> did with an unload request.</summary>
public enum EndpointUnload
{
    /// <summary>The server is not of this control's kind: the next control is asked.</summary>
    NotMine = 0,

    /// <summary>The model is gone from the server's memory (or was not in it).</summary>
    Unloaded = 1,

    /// <summary>The server refused, did not answer, or still lists the model after the wait: it may still hold the graphics card.</summary>
    Failed = 2,
}
