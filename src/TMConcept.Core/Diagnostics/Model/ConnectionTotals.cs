namespace TMConcept.Core.Diagnostics;

/// <summary>Database connections opened (<paramref name="Count"/>) and the time opening them took (<paramref name="Opening"/>).</summary>
public readonly record struct ConnectionTotals(long Count, TimeSpan Opening)
{
    /// <summary>The connections of <paramref name="after"/> that <paramref name="before"/> did not have yet.</summary>
    public static ConnectionTotals operator -(ConnectionTotals after, ConnectionTotals before) =>
        new(after.Count - before.Count, after.Opening - before.Opening);

    /// <summary>The same as the minus operator.</summary>
    public static ConnectionTotals Subtract(ConnectionTotals after, ConnectionTotals before) => after - before;
}
