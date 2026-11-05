using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TMConcept.Desktop.Views;

/// <summary>A bottom-right notification window: no frame, always on top, does not take focus, not in the taskbar.</summary>
public sealed partial class ToastWindow : Window
{
    private const int EdgeGap = 16;

    /// <summary>Creates.</summary>
    public ToastWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>Places the window <paramref name="offset"/> pixels above the bottom-right corner of the work area; returns the used height.</summary>
    public int PlaceAt(int offset)
    {
        if (Screens.Primary is not { } screen)
        {
            return 0;
        }

        var scaling = screen.Scaling;
        var width = (int)Math.Ceiling(Width * scaling);
        var height = (int)Math.Ceiling((Bounds.Height > 0 ? Bounds.Height : 90) * scaling);
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.Right - width - EdgeGap, area.Bottom - height - EdgeGap - offset);
        return height + (int)(8 * scaling);
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        PlaceAt(0);
    }
}
