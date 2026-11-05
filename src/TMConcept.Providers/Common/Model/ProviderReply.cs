namespace TMConcept.Providers.Common;

/// <summary>A successful provider answer.</summary>
public sealed record ProviderReply(byte[] Body, string MediaType, string RequestId)
{
    /// <summary>The body as UTF-8 text.</summary>
    public string Text => System.Text.Encoding.UTF8.GetString(Body);
}
