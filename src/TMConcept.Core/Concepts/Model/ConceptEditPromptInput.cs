using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>What the prompt of an AI edit is built from: the channel, its sample images, the concept as it is now and the user's wish.</summary>
public sealed record ConceptEditPromptInput(Profile Profile, IReadOnlyList<ProfileImage> Images, string Title, string Summary, string Content, string Instruction);
