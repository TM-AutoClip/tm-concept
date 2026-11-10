using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>Reads a profile's past for the prompts (<see cref="PromptMemory"/>): its concepts and its AI-edit wishes, condensed locally.</summary>
public interface IPromptMemorySource
{
    /// <summary>The profile's past as the prompts send it.</summary>
    Task<PromptMemory> ReadAsync(Profile profile, CancellationToken cancellationToken);
}
