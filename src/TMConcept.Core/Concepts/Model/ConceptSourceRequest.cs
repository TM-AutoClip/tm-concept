using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// One batch asked of an <see cref="IConceptSource"/>: the profile, the brief, the model, the batch's place (<see cref="Offset"/>,
/// <see cref="Size"/>), the tokens the prompt already takes without the source's material (<see cref="PromptTokens"/>), the batch's expected
/// output at the high end (<see cref="OutputTokens"/>, thinking included) and its answer limit (<see cref="MaxOutputTokens"/>).
/// <see cref="Estimate"/>: only the price is wanted (nothing slow, no model of this computer is run).
/// </summary>
public sealed record ConceptSourceRequest(Profile Profile, ConceptBrief Brief, CatalogModel Model, int Offset, int Size, long PromptTokens, long OutputTokens,
    int MaxOutputTokens, bool Estimate);
