namespace TMConcept.Storage.Keys;

/// <summary>Content of the key file: which protector sealed the database key and its sealed form (base64).</summary>
public sealed record KeyEnvelope(string Method, string Payload);
