namespace TMConcept.Core.Models;

/// <summary>What a model can do (flags; new values are added at the end).</summary>
[Flags]
public enum ModelCapability
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Text chat.</summary>
    Chat = 1,

    /// <summary>Answers that follow a JSON schema.</summary>
    Schema = 2,

    /// <summary>Reads images.</summary>
    Vision = 4,

    /// <summary>Makes images.</summary>
    Image = 8,

    /// <summary>Makes speech.</summary>
    Speech = 16,

    /// <summary>Makes embedding vectors.</summary>
    Embedding = 32,
}
