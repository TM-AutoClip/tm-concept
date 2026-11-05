namespace TMConcept.Core.Runs;

/// <summary>One group of model calls with its tokens and cost: the source of the exact cost shown to the user.</summary>
public sealed class Run
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Profile (may be empty for a connection test).</summary>
    public Guid? ProfileId { get; set; }

    /// <summary>Kind.</summary>
    public RunKind Kind { get; set; }

    /// <summary>Provider code.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model id.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Label of the API key used (never the key).</summary>
    public string KeyLabel { get; set; } = string.Empty;

    /// <summary>Requested items (concepts).</summary>
    public int Requested { get; set; }

    /// <summary>Accepted items.</summary>
    public int Accepted { get; set; }

    /// <summary>Items dropped as duplicates.</summary>
    public int RejectedDuplicates { get; set; }

    /// <summary>Extra prompt of the user.</summary>
    public string ExtraPrompt { get; set; } = string.Empty;

    /// <summary>Lower end of the estimate (USD).</summary>
    public decimal EstimateMinUsd { get; set; }

    /// <summary>Upper end of the estimate (USD).</summary>
    public decimal EstimateMaxUsd { get; set; }

    /// <summary>Actual cost (USD).</summary>
    public decimal ActualUsd { get; set; }

    /// <summary>Whether the actual cost is exact (from reported usage) or computed from unit prices.</summary>
    public bool CostExact { get; set; }

    /// <summary>Input tokens (cache reads not included).</summary>
    public long InputTokens { get; set; }

    /// <summary>Input tokens read from the prompt cache.</summary>
    public long CachedTokens { get; set; }

    /// <summary>Input tokens written to the prompt cache.</summary>
    public long CacheWriteTokens { get; set; }

    /// <summary>Output tokens (thinking included).</summary>
    public long OutputTokens { get; set; }

    /// <summary>Started (UTC).</summary>
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Finished (UTC).</summary>
    public DateTimeOffset? FinishedAtUtc { get; set; }

    /// <summary>State.</summary>
    public RunStatus Status { get; set; } = RunStatus.Running;

    /// <summary>Error text (redacted).</summary>
    public string Error { get; set; } = string.Empty;
}
