namespace TMConcept.Core.Concepts;

/// <summary>
/// "Suggest topics" was canceled while its call ran. <see cref="EstimatedUsd"/> is what was counted for it in the month's spending (the
/// prompt at least: the provider may bill it).
/// </summary>
public sealed class TopicsCanceledException : OperationCanceledException
{
    /// <summary>Creates.</summary>
    public TopicsCanceledException()
    {
    }

    /// <summary>Creates.</summary>
    public TopicsCanceledException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public TopicsCanceledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates with the estimated cost counted for the canceled call.</summary>
    public TopicsCanceledException(decimal estimatedUsd)
        : base("Topic suggestions were canceled.")
    {
        EstimatedUsd = estimatedUsd;
    }

    /// <summary>The estimated cost counted for the canceled call (USD).</summary>
    public decimal EstimatedUsd { get; }
}
