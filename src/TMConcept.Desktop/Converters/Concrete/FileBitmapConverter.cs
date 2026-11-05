using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TMConcept.Desktop.Converters;

/// <summary>
/// Loads a preview bitmap from a file path, decoded to 240 px wide (or the width given as the converter parameter, the player's poster);
/// <c>null</c> when the file is missing or not an image (logged). Pages pass about twice the width they draw (sharp on high-DPI screens), so no
/// picture is decoded much larger than it is shown.
/// </summary>
public sealed class FileBitmapConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static FileBitmapConverter Instance { get; } = new();

    /// <summary>The log (set at start: XAML creates the converter without services).</summary>
    public static ILogger Logger { get; set; } = NullLogger.Instance;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var width = parameter is string text && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var wanted) ? wanted : 240;
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // The file name only: the full path would name the user's folders in the log.
            Logger.LogWarning(ex, "A preview picture could not be loaded ({File})", Path.GetFileName(path));
            return null;
        }
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
