using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Videos;

/// <summary>
/// The rows of the made videos of a profile, for every page that lists them (the Produced page with all versions, the Concept → video page
/// with the newest version of each video): one <see cref="VideoRowViewModel"/> per version, newest first. A row that a page already
/// shows is used again (its thumbnail made again in the background stays), and a version whose folder is outside the library is left out.
/// </summary>
public sealed class VideoRows(IVideoStore videos, IAppPaths paths, Localizer text, ILogger<VideoRows> logger)
{
    /// <summary>Every version of <paramref name="profileId"/>'s videos, newest first; rows in <paramref name="kept"/> are used again.</summary>
    public async Task<IReadOnlyList<VideoRowViewModel>> ListAsync(Guid profileId, IEnumerable<VideoRowViewModel> kept, CancellationToken cancellationToken)
    {
        var rows = kept.GroupBy(r => r.Video.Id).ToDictionary(g => g.Key, g => g.First());
        var list = await videos.ListAsync(profileId, cancellationToken).ConfigureAwait(true);
        var result = new List<VideoRowViewModel>(list.Count);
        foreach (var video in list)
        {
            if (rows.TryGetValue(video.Id, out var row))
            {
                result.Add(row);
                continue;
            }

            try
            {
                result.Add(new VideoRowViewModel(video, LibraryPaths.Resolve(paths.LibraryDirectory, video.Folder), text));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "A video folder is outside the library and is ignored");
            }
        }

        return result;
    }

    /// <summary>The newest version of each video (<paramref name="rows"/> newest first), at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<VideoRowViewModel> Latest(IEnumerable<VideoRowViewModel> rows, int max = int.MaxValue) =>
        rows.GroupBy(r => r.GroupKey, StringComparer.Ordinal).Select(g => g.First()).Take(max).ToList();

    /// <summary>The newest version of each of <paramref name="profileId"/>'s videos, newest first, at most <paramref name="max"/>.</summary>
    public async Task<IReadOnlyList<VideoRowViewModel>> LatestAsync(Guid profileId, IEnumerable<VideoRowViewModel> kept, int max,
        CancellationToken cancellationToken) => Latest(await ListAsync(profileId, kept, cancellationToken).ConfigureAwait(true), max);
}
