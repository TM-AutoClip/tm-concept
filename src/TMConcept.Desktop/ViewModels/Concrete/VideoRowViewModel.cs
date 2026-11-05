using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A produced video in the list (its thumbnail can be made again while it is shown: <see cref="RefreshThumbnail"/>).</summary>
public sealed class VideoRowViewModel(VideoRecord video, string folder, Localizer text) : ObservableObject
{
    /// <summary>The record.</summary>
    public VideoRecord Video { get; } = video;

    /// <summary>The video folder.</summary>
    public string Folder { get; } = folder;

    /// <summary>Title.</summary>
    public string Title => Video.Title;

    /// <summary>
    /// Thumbnail path, "" when there is none or its file is gone (so the value changes when it is made again and the bindings load it).
    /// </summary>
    public string Thumbnail => Video.ThumbnailName.Length > 0 && System.IO.File.Exists(Path.Combine(Folder, Video.ThumbnailName))
        ? Path.Combine(Folder, Video.ThumbnailName)
        : string.Empty;

    /// <summary>The video file.</summary>
    public string File => Path.Combine(Folder, Video.FileName);

    /// <summary>"3 Nov 2026 · 0:48 · 0.21 $".</summary>
    public string Line => Video.CreatedAtUtc.ToLocalTime().ToString("g", text.Culture) + " · "
        + TimeSpan.FromMilliseconds(Video.DurationMs).ToString(@"m\:ss", CultureInfo.InvariantCulture) + " · " + text.Usd(Video.TotalUsd);

    /// <summary>Warning badges ("file missing" first when the video file was deleted from outside).</summary>
    public string Badges => string.Join(" ", (Missing ? [text["produced.badge.missing"]] : Array.Empty<string>())
        .Concat(Video.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(w => text["produced.badge." + w])));

    /// <summary>Whether the thumbnail file is there.</summary>
    public bool HasThumbnailFile => Thumbnail.Length > 0;

    /// <summary>Whether the files are still there.</summary>
    public bool Missing => !System.IO.File.Exists(File);

    /// <summary>The thumbnail file was made again: the list shows it.</summary>
    public void RefreshThumbnail() => OnPropertyChanged(nameof(Thumbnail));
}
