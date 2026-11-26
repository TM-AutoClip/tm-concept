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

    /// <summary>
    /// Describing a sample image with a vision model. Not written since 0.1.29 (sample images were removed); the number stays taken, so the
    /// runs of older data keep their kind and the other numbers do not move.
    /// </summary>
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

    /// <summary>An AI edit of one concept on the concept page (one chat call with the concept step's model).</summary>
    ConceptEdit = 9,

    /// <summary>"Find topics" in the profile's documents on the concept page (one chat call with the topics step's model, 0.1.21).</summary>
    DocumentTopics = 10,

    /// <summary>Reading the text of scanned PDF pages with a model that reads images, while a document is added (0.1.24).</summary>
    DocumentText = 11,

    /// <summary>Summarising the chosen pages of the documents before a "read everything" concept run (0.1.24).</summary>
    DocumentDigest = 12,

    /// <summary>"Kanaldan esinlen" on the profile page: one chat call that writes a channel content draft (0.1.53; no profile yet).</summary>
    ChannelInspiration = 13,

    /// <summary>A piece made by an online music model on the "Müzik üret" page (0.1.55; a video's music is in the video's own cost lines).</summary>
    Music = 14,
}
