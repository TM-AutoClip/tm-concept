namespace TMConcept.Core.Documents;

/// <summary>Where adding a document is (shown while it is added).</summary>
public enum DocumentStage
{
    /// <summary>Copying the file, or fetching the page or article.</summary>
    Reading,

    /// <summary>Reading its text.</summary>
    Extracting,

    /// <summary>Cutting the text into passages and saving them.</summary>
    Chunking,

    /// <summary>The meaning search's vectors of the passages (only when its model is installed).</summary>
    Embedding,

    /// <summary>Done.</summary>
    Done,
}
