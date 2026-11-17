namespace TMConcept.Core.Documents;

/// <summary>
/// One passage in a prompt: the number of its document, where it is ("p. 12", "Trade"), its text and the id of the stored passage (so a run
/// can send other passages in its next batch; empty when not known).
/// </summary>
public sealed record GroundingPassage(int Document, string Locator, string Text, Guid ChunkId = default);
