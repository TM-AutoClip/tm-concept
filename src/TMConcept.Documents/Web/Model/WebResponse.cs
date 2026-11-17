namespace TMConcept.Documents.Web;

/// <summary>A fetched body (within the size limit), its media type and charset (empty when not given) and the address it finally came from.</summary>
public sealed record WebResponse(byte[] Body, string MediaType, string Charset, Uri Address);
