using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Profiles;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A sample image row of the profile editor.</summary>
public sealed partial class ProfileImageViewModel(ProfileImage image, string fullPath) : ObservableObject
{
    /// <summary>The record.</summary>
    public ProfileImage Image { get; } = image;

    /// <summary>Full path (preview).</summary>
    public string FullPath { get; } = fullPath;

    /// <summary>File name.</summary>
    public string FileName => Path.GetFileName(FullPath);

    /// <summary>Size text.</summary>
    public string Size => Image.Width + "×" + Image.Height;

    /// <summary>Caption the user writes.</summary>
    [ObservableProperty]
    public partial string Caption { get; set; } = image.Caption;
}
