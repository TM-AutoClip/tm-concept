namespace TMConcept.Core.AppOllama;

/// <summary>
/// One line of Ollama's <c>POST /api/pull</c> stream: <c>{"status":"pulling …","digest":"sha256:…","total":…,"completed":…}</c>; the last one
/// is <c>{"status":"success"}</c>, a failure <c>{"error":"…"}</c>.
/// </summary>
/// <param name="Status">The status text (<c>pulling manifest</c>, <c>pulling &lt;digest&gt;</c>, <c>verifying sha256 digest</c>, <c>success</c>).</param>
/// <param name="Digest">The layer the line is about (empty for a status line).</param>
/// <param name="Total">Bytes of that layer (0 = not said).</param>
/// <param name="Completed">Bytes of that layer received so far (0 = not said).</param>
/// <param name="Error">The error Ollama sent (empty = none).</param>
public sealed record AppModelPullProgress(string Status, string Digest, long Total, long Completed, string Error = "")
{
    /// <summary>Whether this is the closing <c>success</c> line.</summary>
    public bool Success => string.Equals(Status, "success", StringComparison.Ordinal);
}
