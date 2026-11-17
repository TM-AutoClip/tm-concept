using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.AppOllama;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A recommended model of the app's own Ollama copy on Settings → Local AI: name and tag, its download size, whether it fits this card
/// (green) or not (red), its license (orange when not for commercial use) and whether it is installed.
/// </summary>
public sealed partial class AppModelRowViewModel(AppModelInfo model, string size, string fitText, AppModelFit fit, string license, bool installed)
    : ObservableObject
{
    /// <summary>The model.</summary>
    public AppModelInfo Model { get; } = model;

    /// <summary>"Gemma 4 E4B".</summary>
    public string Name => Model.Name;

    /// <summary>The exact Ollama tag.</summary>
    public string Tag => Model.Tag;

    /// <summary>"6.1 GB" (the download).</summary>
    public string Size { get; } = size;

    /// <summary>"Fits your card (needs about 6.3 GB)" / "Larger than your card…".</summary>
    public string FitText { get; } = fitText;

    /// <summary>Fits on the card (green).</summary>
    public bool Fits => fit == AppModelFit.Fits;

    /// <summary>Does not fit, or runs on the processor (red).</summary>
    public bool DoesNotFit => fit != AppModelFit.Fits;

    /// <summary>The license line.</summary>
    public string License { get; } = license;

    /// <summary>A license that limits commercial use (orange).</summary>
    public bool LicenseWarning => Model.LicenseUse != Core.Models.LicenseUse.Commercial;

    /// <summary>Installed and complete.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    public partial bool Installed { get; set; } = installed;

    /// <summary>Not installed: the Download button shows.</summary>
    public bool CanDownload => !Installed;
}
