namespace TMConcept.Core.Chat;

/// <summary>An image sent to a vision model.</summary>
public sealed record ImagePart(byte[] Data, string MimeType);
