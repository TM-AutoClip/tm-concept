namespace TMConcept.Providers.Common;

/// <summary>A streamed answer that ended normally: the provider's request id, the characters read and when the first line came.</summary>
public sealed record StreamReply(string RequestId, long Characters, long FirstLineMs, string MediaType);
