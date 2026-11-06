namespace TMConcept.Core.Components;

/// <summary>One download of an install: what it is, the host it comes from and about how many bytes.</summary>
public sealed record ComponentDownload(string Name, string Host, long ApproximateBytes);
