namespace TMConcept.Core.Documents;

/// <summary>
/// The meaning search: the cosine of the query's vector and each passage's stored vector (both normalized, so their dot product). Runs only
/// with a query vector and stored vectors; a passage without a vector is not ranked here (the keyword search still ranks it).
/// </summary>
public sealed class CosineRanker : PassageRankerBase
{
    /// <inheritdoc />
    protected override bool CanRank(DocumentSearch search) => search.QueryVector is { Length: > 0 } && search.Vectors.Count > 0;

    /// <inheritdoc />
    protected override IEnumerable<RankedChunk> Score(DocumentSearch search)
    {
        var query = search.QueryVector!;
        foreach (var chunk in search.Chunks)
        {
            if (search.Vectors.TryGetValue(chunk.Id, out var vector) && vector.Length == query.Length)
            {
                yield return new RankedChunk(chunk, Dot(query, vector));
            }
        }
    }

    /// <summary>The dot product.</summary>
    public static double Dot(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var sum = 0.0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            sum += a[i] * (double)b[i];
        }

        return sum;
    }
}
