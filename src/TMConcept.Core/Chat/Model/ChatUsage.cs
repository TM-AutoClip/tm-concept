namespace TMConcept.Core.Chat;

/// <summary>Tokens of one call: uncached input, cache reads, cache writes and output (thinking included).</summary>
public sealed record ChatUsage(long Input, long CacheRead, long CacheWrite, long Output)
{
    /// <summary>No tokens.</summary>
    public static ChatUsage Zero { get; } = new(0, 0, 0, 0);

    /// <summary>The prompt size (all input kinds).</summary>
    public long Prompt => Input + CacheRead + CacheWrite;

    /// <summary>Sum of two usages.</summary>
    public ChatUsage Plus(ChatUsage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new ChatUsage(Input + other.Input, CacheRead + other.CacheRead, CacheWrite + other.CacheWrite, Output + other.Output);
    }
}
