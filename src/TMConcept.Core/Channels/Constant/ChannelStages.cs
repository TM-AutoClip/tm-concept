namespace TMConcept.Core.Channels;

/// <summary>The steps of "Kanaldan esinlen" (<see cref="ChannelProgress.Stage"/>; the interface names them "inspire.stage.&lt;stage&gt;").</summary>
public static class ChannelStages
{
    /// <summary>yt-dlp reads the channel's public details.</summary>
    public const string List = "list";

    /// <summary>A sample video is downloaded and measured (then deleted).</summary>
    public const string Sample = "sample";

    /// <summary>The channel photo is fetched (only when asked for).</summary>
    public const string Photo = "photo";

    /// <summary>The numbers are put together into a draft.</summary>
    public const string Draft = "draft";

    /// <summary>The text model writes the channel's content (one call; only when chosen).</summary>
    public const string Writing = "writing";
}
