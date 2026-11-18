using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Documents;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A PDF picked for "Belgelerim" that waits for the user's choice before it is added (0.1.24): its page count and how much of it looks
/// scanned (<see cref="DocumentProbe"/>), the pages to add (<see cref="Range"/>: empty = all, or "120-180"), whether its image pages are
/// read by the text extraction model, and the estimate of reading it (filled by the library panel).
/// </summary>
public sealed partial class DocumentImportViewModel(string file, DocumentProbe probe, long bytes) : ObservableObject
{
    /// <summary>The picked file.</summary>
    public string File { get; } = file;

    /// <summary>Its name.</summary>
    public string Name { get; } = Path.GetFileName(file);

    /// <summary>What the quick look found.</summary>
    public DocumentProbe Probe { get; } = probe;

    /// <summary>Its size in bytes.</summary>
    public long Bytes { get; } = bytes;

    /// <summary>The pages to add as the user typed them (empty = all).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeValid), nameof(PagesChosen), nameof(ImagePages))]
    public partial string Range { get; set; } = string.Empty;

    /// <summary>Read the image pages with the text extraction model.</summary>
    [ObservableProperty]
    public partial bool Extract { get; set; }

    /// <summary>"700 pages · 85.2 MB".</summary>
    [ObservableProperty]
    public partial string Info { get; set; } = string.Empty;

    /// <summary>"About 640 of the 700 pages look like images" (empty when none does).</summary>
    [ObservableProperty]
    public partial string Scanned { get; set; } = string.Empty;

    /// <summary>The estimate of adding it as chosen now, or why the range is not valid.</summary>
    [ObservableProperty]
    public partial string Estimate { get; set; } = string.Empty;

    /// <summary>The estimate line is a problem (an invalid range).</summary>
    [ObservableProperty]
    public partial bool EstimateIsError { get; set; }

    /// <summary>Whether a text extraction model can be used (one is chosen on the page).</summary>
    [ObservableProperty]
    public partial bool CanExtract { get; set; }

    /// <summary>Whether its sampled pages showed image pages (the extraction choice is shown).</summary>
    public bool HasImagePages => Probe.HasImagePages;

    /// <summary>Whether <see cref="Range"/> is a range of this file (or empty).</summary>
    public bool RangeValid => DocumentPageRange.TryParse(Range, Probe.Pages, out _);

    /// <summary>The chosen range (null = every page, or an invalid range).</summary>
    public DocumentPageRange? Pages => DocumentPageRange.TryParse(Range, Probe.Pages, out var range) ? range : null;

    /// <summary>Pages that would be read.</summary>
    public int PagesChosen => Pages?.Count ?? Probe.Pages;

    /// <summary>About how many of the chosen pages are images.</summary>
    public int ImagePages => Probe.ImagePagesAbout(PagesChosen);
}
