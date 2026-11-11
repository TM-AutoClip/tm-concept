using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// A concept run: profile, what is asked (<see cref="ConceptBrief"/>: the ticked topics or an idea with a count), the resolved model, whether
/// the word-similarity check runs, the optional meaning check model and the chosen focus (<see cref="FocusText"/>; empty = none).
/// </summary>
public sealed record ConceptRequest(Profile Profile, ConceptBrief Brief, ResolvedModel Model, bool LocalCheck = true, ResolvedModel? Embedding = null,
    string Focus = "");
