namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One missing piece before production on the video page (F16, 0.1.49): the gap, its text, whether it turns "Produce" off, whether it can be
/// skipped and the label of its fix button (empty = none).
/// </summary>
public sealed class GapRowViewModel(Core.Videos.ProductionGap gap, string text, string fix)
{
    /// <summary>The gap.</summary>
    public Core.Videos.ProductionGap Gap { get; } = gap;

    /// <summary>What is missing, in words.</summary>
    public string Text { get; } = text;

    /// <summary>It turns "Produce" off.</summary>
    public bool Required { get; } = gap.Kind == Core.Videos.GapKind.Required;

    /// <summary>It can be skipped on purpose.</summary>
    public bool CanSkip { get; } = gap.Kind == Core.Videos.GapKind.Optional;

    /// <summary>The fix button's label (empty = no button).</summary>
    public string Fix { get; } = fix;

    /// <summary>Whether the fix button shows.</summary>
    public bool CanFix { get; } = fix.Length > 0;
}
