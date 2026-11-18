namespace TMConcept.Core.Documents;

/// <summary>A kept page without its text (0.1.25): its file, its number, its length and what read it.</summary>
public sealed record DocumentPageMark(Guid ProfileId, string FileSha256, int Page, int Characters, string Source);
