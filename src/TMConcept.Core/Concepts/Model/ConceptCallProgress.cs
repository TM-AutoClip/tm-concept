using TMConcept.Core.Chat;

namespace TMConcept.Core.Concepts;

/// <summary>
/// The live state of a concept batch's model call, for the screen: which batch of how many, its size, the call's step
/// (<see cref="ChatProgress"/>) and how many concepts of the batch are complete in the answer so far (<see cref="Ready"/>).
/// </summary>
public sealed record ConceptCallProgress(int Batch, int Batches, int Size, ChatProgress Call, int Ready);
