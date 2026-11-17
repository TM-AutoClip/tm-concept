namespace TMConcept.Core.Research;

/// <summary>A search or a page that failed: where (a backend or a page's title), the code the page turns into the user's language, and the detail.</summary>
public sealed record ResearchFailure(string Source, string Code, string Detail);
