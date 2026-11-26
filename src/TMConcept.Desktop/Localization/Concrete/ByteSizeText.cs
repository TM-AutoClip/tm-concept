using System.Globalization;

namespace TMConcept.Desktop.Localization;

/// <summary>
/// A size on screen: "1.2 GB", "312 MB", "4 KB" in the app's culture (0.1.55: it was a helper of the storage settings' view model, which the
/// start's checks and the dialogs called across the layers).
/// </summary>
public static class ByteSizeText
{
    private const double Kilo = 1024;

    /// <summary>"1.2 GB", "312 MB", "4 KB".</summary>
    public static string Of(long bytes, CultureInfo culture) => bytes switch
    {
        >= 1L << 30 => (bytes / (Kilo * Kilo * Kilo)).ToString("0.0", culture) + " GB",
        >= 1L << 20 => Math.Round(bytes / (Kilo * Kilo)).ToString("0", culture) + " MB",
        _ => Math.Ceiling(bytes / Kilo).ToString("0", culture) + " KB",
    };
}
