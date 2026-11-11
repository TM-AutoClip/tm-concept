using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// A "Generate" press: profile, how many, the extra request, the resolved model, whether the word-similarity check runs, the optional
/// meaning check model, the chosen focus (<see cref="FocusText"/>; empty = none) and the selected topics (one concept each, in order; when
/// there are any, <see cref="Count"/> is ignored and the count is the number of topics, at most <see cref="ConceptLimits.CountMax"/>).
/// </summary>
public sealed record ConceptRequest(Profile Profile, int Count, string ExtraPrompt, ResolvedModel Model, bool LocalCheck = true, ResolvedModel? Embedding = null,
    string Focus = "", IReadOnlyList<string>? Topics = null);
