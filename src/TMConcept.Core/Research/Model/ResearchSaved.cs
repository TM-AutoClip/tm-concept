using TMConcept.Core.Documents;

namespace TMConcept.Core.Research;

/// <summary>
/// The fetched pages saved into the profile's documents: every usable document (already there ones included), the ids this run added (only
/// these are taken away again when the user does not keep the results), the pages that failed or were left out, the bytes fetched and
/// whether a limit (bytes or time) stopped the fetching early.
/// </summary>
public sealed record ResearchSaved(IReadOnlyList<SourceDocument> Documents, IReadOnlyList<Guid> Added, IReadOnlyList<ResearchFailure> Skipped, long Bytes, bool Cut);
