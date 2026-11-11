using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace TMConcept.Tests.Desktop.Screenshots;

/// <summary>Saves what a headless window drew (Skia on the processor) as a PNG.</summary>
internal static class ScreenshotWriter
{
    /// <summary>Lays out <paramref name="window"/> once more and saves its frame to <paramref name="path"/> (its folder is created).</summary>
    public static void Save(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window drew no frame.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}
