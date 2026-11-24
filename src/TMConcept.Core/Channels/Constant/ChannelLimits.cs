namespace TMConcept.Core.Channels;

/// <summary>
/// The limits of "Kanaldan esinlen" (0.1.53, the developer's decision of 24 Nov 2026): a channel's public information is read with yt-dlp
/// (no cookies, no sign-in, no API key; whose channel it is is never checked), a few of its videos are measured like a reference video
/// (<see cref="References.IReferenceMeasurer"/>) and deleted right after, and only numbers and a draft profile are kept.
/// </summary>
public static class ChannelLimits
{
    /// <summary>The newest videos of the channel whose public details are read (title, length, a short description, the upload date).</summary>
    public const int ListCount = 30;

    /// <summary>Sample videos measured by default.</summary>
    public const int SamplesDefault = 3;

    /// <summary>Fewest sample videos.</summary>
    public const int SamplesMin = 1;

    /// <summary>Most sample videos.</summary>
    public const int SamplesMax = 5;

    /// <summary>A sample shorter than this is not measured (seconds; a reference needs <see cref="References.ReferenceLimits.MinSeconds"/>, ours a little more).</summary>
    public const double SampleMinSeconds = 5;

    /// <summary>When the channel's address lists its tabs instead of videos (a YouTube channel's home), at most this many tabs are read.</summary>
    public const int TabsMax = 2;

    /// <summary>Longest title kept of a video (characters).</summary>
    public const int TitleMax = 140;

    /// <summary>Longest description kept of a video (characters; only the start says something about the style).</summary>
    public const int DescriptionMax = 280;

    /// <summary>Longest channel description kept (characters).</summary>
    public const int ChannelDescriptionMax = 1000;

    /// <summary>Longest channel name kept (characters; the profile's own limit).</summary>
    public const int NameMax = Profiles.ProfileLimits.NameMax;

    /// <summary>Only the first this many videos send their description to the model (the titles of all of them go).</summary>
    public const int PromptDescriptions = 8;

    /// <summary>The channel photo (its thumbnail) is at most this large (bytes).</summary>
    public const long PhotoMaxBytes = 4 * 1024 * 1024;

    /// <summary>The temporary folder of a channel's look, under the work folder (deleted afterwards, and at every start).</summary>
    public const string Folder = "channel";

    /// <summary>Roof of the model's answer (tokens, thinking included; lowered to the model's own limit).</summary>
    public const int OutputMax = 6000;

    /// <summary>Output tokens of a usual answer (the high end of the estimate).</summary>
    public const int OutputExpected = 1800;

    /// <summary>Fewest output tokens of an answer (the low end of the estimate).</summary>
    public const int OutputMin = 300;
}
