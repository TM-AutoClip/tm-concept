using TMConcept.Core.Chat;

namespace TMConcept.Core.Documents;

/// <summary>The images of one page without a text layer (a scan), the largest first, for a text-extraction model.</summary>
public sealed record PageImage(int Page, IReadOnlyList<ImagePart> Images);
