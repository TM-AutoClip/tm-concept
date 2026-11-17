using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The 👍 / 👎 of one made video under its preview, on the Produced page and in Concept → video (17 Nov 2026, the developer). All versions of
/// a video share one (<see cref="VideoRater"/>). Pressing the vote it already has clears it. A failed save is logged, shown in red under the
/// buttons and leaves the vote as it was.
/// </summary>
public sealed partial class VideoRatingViewModel(IVideoRater rater, VideoRecord video, VideoVote? vote, Localizer text, ILogger logger) : ObservableObject
{
    /// <summary>The video the rating is saved for (any version of it).</summary>
    public VideoRecord Video { get; } = video;

    /// <summary>The current vote (null = not rated).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiked), nameof(IsDisliked))]
    public partial VideoVote? Vote { get; set; } = vote;

    /// <summary>Why the last press was not saved (empty = it was).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether a save is running (the buttons wait).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LikeCommand), nameof(DislikeCommand))]
    public partial bool Busy { get; set; }

    /// <summary>Liked.</summary>
    public bool IsLiked => Vote == VideoVote.Like;

    /// <summary>Disliked.</summary>
    public bool IsDisliked => Vote == VideoVote.Dislike;

    /// <summary>Whether <see cref="Error"/> is shown.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>"I liked it" (again = clears the rating).</summary>
    [RelayCommand(CanExecute = nameof(CanRate))]
    public Task LikeAsync() => RateAsync(VideoVote.Like);

    /// <summary>"I did not like it" (again = clears the rating).</summary>
    [RelayCommand(CanExecute = nameof(CanRate))]
    public Task DislikeAsync() => RateAsync(VideoVote.Dislike);

    private bool CanRate() => !Busy;

    private async Task RateAsync(VideoVote pressed)
    {
        Busy = true;
        Error = string.Empty;
        try
        {
            Vote = await rater.RateAsync(Video, pressed, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The video rating could not be saved");
            Error = text.Format("video.rating.failed", ex.Message);
            // The pressed toggle button checked itself: it shows the vote that stayed.
            OnPropertyChanged(nameof(Vote));
            OnPropertyChanged(nameof(IsLiked));
            OnPropertyChanged(nameof(IsDisliked));
        }
        finally
        {
            Busy = false;
        }
    }
}
