using Avalonia.Controls;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The concept page's fine-tuning (tone, audience, telling) folded into one line, shared by topic suggestions and both generate sections;
/// opening it shows the drop-downs under the page's bar.
/// </summary>
public sealed partial class FineTuneBar : UserControl
{
    /// <summary>Creates.</summary>
    public FineTuneBar() => InitializeComponent();
}
