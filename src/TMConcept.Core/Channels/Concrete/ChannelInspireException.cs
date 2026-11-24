namespace TMConcept.Core.Channels;

/// <summary>
/// "Kanaldan esinlen" could not go on (<see cref="Code"/>: "address" (not a web address), "missing" (yt-dlp is not installed), "list" (yt-dlp
/// could not read the channel), "empty" (no video was listed), "answer" (the model's answer had no channel content)). The message is in English
/// for the log; the interface shows its own text for the code with <see cref="Detail"/>.
/// </summary>
public sealed class ChannelInspireException : Exception
{
    /// <summary>Creates.</summary>
    public ChannelInspireException()
    {
    }

    /// <summary>Creates.</summary>
    public ChannelInspireException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public ChannelInspireException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Failure code.</summary>
    public string Code { get; init; } = "list";

    /// <summary>What the tool said (its last lines, folders removed), or empty.</summary>
    public string Detail { get; init; } = string.Empty;
}
