using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace TMConcept.Desktop.Converters;

/// <summary>Loads a small preview bitmap from a file path (decoded to 240 px wide); <c>null</c> when the file is missing or not an image.</summary>
public sealed class FileBitmapConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static FileBitmapConverter Instance { get; } = new();

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
            return Bitmap.DecodeToWidth(stream, 240);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("Preview could not be loaded: " + ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
