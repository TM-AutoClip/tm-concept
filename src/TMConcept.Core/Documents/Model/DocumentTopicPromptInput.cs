using TMConcept.Core.Concepts;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Documents;

/// <summary>What the "Find topics" prompt of the documents section is built from (<see cref="Language"/>: the interface language).</summary>
public sealed record DocumentTopicPromptInput(Profile Profile, PromptMemory Memory, string Subject, DocumentGrounding Grounding, int Count, string Language);
