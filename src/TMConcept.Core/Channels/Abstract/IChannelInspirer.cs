namespace TMConcept.Core.Channels;

/// <summary>
/// "Kanaldan esinlen" (0.1.53, the developer's decision of 24 Nov 2026): a channel's address → its public details, a few sample videos measured
/// like a reference video and deleted right after, and the numbers of a draft profile. Whose channel it is is never checked. The channel's
/// address and what it says are never written to the log in full (only the steps and numbers).
/// </summary>
public interface IChannelInspirer
{
    /// <summary>The temporary folders of the looks running or waiting for <see cref="Discard"/> now (the start-up sweep leaves them).</summary>
    IReadOnlyCollection<string> ActiveFolders { get; }

    /// <summary>yt-dlp is installed.</summary>
    bool Ready { get; }

    /// <summary>
    /// Looks at the channel at <paramref name="address"/>: reads its public details, measures at most <paramref name="samples"/> of its videos
    /// (shorter first) and, when <paramref name="photo"/>, fetches its photo. A failed sample is skipped and counted; a cancel or a failure of the
    /// look itself deletes its temporary folder. Throws <see cref="ChannelInspireException"/> ("address", "missing", "list", "empty").
    /// </summary>
    Task<ChannelInspiration> InspectAsync(string address, int samples, bool photo, IProgress<ChannelProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Deletes the look's temporary folder (the photo with it); a folder that cannot be removed now goes at the next start.</summary>
    void Discard(ChannelInspiration inspiration);
}
