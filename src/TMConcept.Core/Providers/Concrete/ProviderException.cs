namespace TMConcept.Core.Providers;

/// <summary>
/// A provider call failed. <see cref="Code"/> says why (<c>auth</c>, <c>billing</c>, <c>rate</c>, <c>busy</c>, <c>model</c>, <c>bad_request</c>,
/// <c>too_large</c>, <c>network</c>, <c>timeout</c>, <c>truncated</c>, <c>refusal</c>, <c>incomplete</c>, <c>http</c>); the interface turns
/// it into text. The message and detail never contain the key.
/// </summary>
public sealed class ProviderException : Exception
{
    /// <summary>Creates.</summary>
    public ProviderException()
    {
    }

    /// <summary>With a message.</summary>
    public ProviderException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and an inner error.</summary>
    public ProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Why.</summary>
    public string Code { get; init; } = "http";

    /// <summary>HTTP status (0 = none).</summary>
    public int Status { get; init; }

    /// <summary>Provider's request id (for support; not a secret).</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>Usage already billed before the failure (a cut-off answer still costs).</summary>
    public Chat.ChatUsage? ChargedUsage { get; init; }

    /// <summary>
    /// The text that arrived before a cut (<c>truncated</c> / <c>incomplete</c>; empty otherwise). It was paid for, so a caller that can use
    /// a partial answer (the concept parser keeps every complete item before the cut) reads it from here.
    /// </summary>
    public string PartialText { get; init; } = string.Empty;
}
