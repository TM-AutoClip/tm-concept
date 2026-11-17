namespace TMConcept.Core.Documents;

/// <summary>
/// The common part of a meaning model of the e5 family: every input carries its role, <c>"query: "</c> for a question or subject and
/// <c>"passage: "</c> for a document passage (the model card says leaving them out makes the search worse), empty inputs are refused, and a
/// model that is not installed says so before anything runs.
/// </summary>
public abstract class DocumentEmbedderBase : IDocumentEmbedder
{
    /// <summary>The prefix of a question or subject.</summary>
    public const string QueryPrefix = "query: ";

    /// <summary>The prefix of a document passage.</summary>
    public const string PassagePrefix = "passage: ";

    /// <inheritdoc />
    public abstract string ModelId { get; }

    /// <inheritdoc />
    public abstract string ComponentCode { get; }

    /// <inheritdoc />
    public abstract bool Ready { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>> EmbedPassagesAsync(IReadOnlyList<string> passages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passages);
        return Check() ? EmbedAsync(passages.Select(p => PassagePrefix + p.Trim()).ToList(), cancellationToken) : Task.FromResult<IReadOnlyList<float[]>>([]);
    }

    /// <inheritdoc />
    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        Check();
        return (await EmbedAsync([QueryPrefix + query.Trim()], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>One normalized vector per text (the texts already carry their prefix).</summary>
    protected abstract Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);

    private bool Check() => Ready
        ? true
        : throw new InvalidOperationException("The meaning search model is not installed; it is installed after a confirmation.");
}
