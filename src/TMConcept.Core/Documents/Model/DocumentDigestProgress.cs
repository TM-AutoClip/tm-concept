namespace TMConcept.Core.Documents;

/// <summary>Where a "read everything" run is: calls done of all (the total may shrink when the notes turn out shorter than planned).</summary>
public sealed record DocumentDigestProgress(int Done, int Total);
