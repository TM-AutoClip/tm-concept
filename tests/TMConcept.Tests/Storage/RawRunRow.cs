namespace TMConcept.Tests.Storage;

/// <summary>A row of <c>runs</c> exactly as stored (no value converters).</summary>
internal sealed class RawRunRow
{
    public string Id { get; set; } = string.Empty;

    public string EstimateMinUsd { get; set; } = string.Empty;

    public string EstimateMaxUsd { get; set; } = string.Empty;

    public string ActualUsd { get; set; } = string.Empty;

    public long CostExact { get; set; }

    public long StartedAtUtc { get; set; }

    public long? FinishedAtUtc { get; set; }

    public long Kind { get; set; }

    public long Status { get; set; }
}
