using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>A "Generate" press: profile, how many, the extra request, the resolved model and whether the local duplicate check runs.</summary>
public sealed record ConceptRequest(Profile Profile, int Count, string ExtraPrompt, ResolvedModel Model, bool LocalCheck = true);
