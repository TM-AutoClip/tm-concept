using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// A "Generate" press: profile, how many, the extra request, the resolved model, whether the word-similarity check runs, the optional
/// meaning check model and the chosen focus (<see cref="FocusText"/>; empty = none).
/// </summary>
public sealed record ConceptRequest(Profile Profile, int Count, string ExtraPrompt, ResolvedModel Model, bool LocalCheck = true, ResolvedModel? Embedding = null,
    string Focus = "");
