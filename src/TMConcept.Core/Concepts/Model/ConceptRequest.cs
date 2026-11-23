using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// A concept run: profile, what is asked (<see cref="ConceptBrief"/>: the ticked topics or an idea with a count), the resolved model, whether
/// the word-similarity check runs, the optional meaning check model and the chosen focus (<see cref="FocusText"/>; empty = none).
/// <see cref="Style"/> (0.1.32): the drawing style the concepts are written for (null = none); it goes into the prompt and onto each concept.
/// </summary>
public sealed record ConceptRequest(Profile Profile, ConceptBrief Brief, ResolvedModel Model, bool LocalCheck = true, ResolvedModel? Embedding = null,
    string Focus = "", Styles.DrawStyle? Style = null);
