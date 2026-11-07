namespace TMConcept.Core.Runs;

/// <summary>What a group of model calls did (new values are added at the end).</summary>
public enum RunKind
{
    /// <summary>Concept generation.</summary>
    Concepts = 0,

    /// <summary>Video code (and its single repair).</summary>
    VideoCode = 1,

    /// <summary>Image prompt writing and image generation.</summary>
    Image = 2,

    /// <summary>Online speech.</summary>
    Speech = 3,

    /// <summary>Embeddings for the semantic duplicate check.</summary>
    Embedding = 4,

    /// <summary>Describing a sample image with a vision model.</summary>
    Describe = 5,

    /// <summary>Connection test of a key.</summary>
    ConnectionTest = 6,

    /// <summary>
    /// What a video that failed or was canceled had paid until then (narration, images, code; a made video keeps its cost in its own
    /// record instead), so the month's spending counts it.
    /// </summary>
    FailedVideo = 7,

    /// <summary>"Suggest topics" on the concept page (one cheap chat call).</summary>
    Topics = 8,
}
