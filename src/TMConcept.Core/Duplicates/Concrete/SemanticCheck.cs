namespace TMConcept.Core.Duplicates;

/// <summary>
/// Layer 3 (optional): cosine similarity of title + summary vectors, computed locally with a plain loop. ≥ 0.90 duplicate, 0.82–0.90 similar
/// (starting values; tuned on real lists).
/// </summary>
public static class SemanticCheck
{
    /// <summary>Duplicate from this cosine.</summary>
    public const double Duplicate = 0.90;

    /// <summary>Similar from this cosine.</summary>
    public const double Similar = 0.82;

    /// <summary>The text that is embedded.</summary>
    public static string Text(string title, string summary) => (title ?? string.Empty).Trim() + ". " + (summary ?? string.Empty).Trim();

    /// <summary>Cosine similarity.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }

        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>The closest earlier concept as a duplicate result (New when below the similar band).</summary>
    public static DuplicateResult Check(float[] candidate, IEnumerable<(DuplicateSubject Subject, float[] Vector)> earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        var best = (Subject: (DuplicateSubject?)null, Score: 0.0);
        foreach (var (subject, vector) in earlier)
        {
            var score = Cosine(candidate, vector);
            if (score > best.Score)
            {
                best = (subject, score);
            }
        }

        if (best.Subject is not { } other)
        {
            return DuplicateResult.None;
        }

        var kind = best.Score >= Duplicate ? DuplicateKind.Duplicate : best.Score >= Similar ? DuplicateKind.Similar : DuplicateKind.New;
        return kind == DuplicateKind.New ? DuplicateResult.None with { Similarity = best.Score } : new DuplicateResult(kind, best.Score, other.Id, other.Number, other.Title);
    }

    /// <summary>Vector → bytes (little-endian floats).</summary>
    public static byte[] ToBytes(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Bytes → vector.</summary>
    public static float[] FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, vector.Length * sizeof(float));
        return vector;
    }
}
