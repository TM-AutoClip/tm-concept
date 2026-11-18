namespace TMConcept.Core.Styles;

/// <summary>How much help text needs over what is behind it (<see cref="TextBackdrop.Need"/>; only with readable text on).</summary>
public enum TextBackdropNeed
{
    /// <summary>Enough contrast and a calm background: the normal outline and shadow of readable text.</summary>
    Plain = 0,

    /// <summary>Some of the background is close to the text's brightness, or it is busy: a thicker outline and a darker shadow.</summary>
    Stronger = 1,

    /// <summary>Much of the background is close to the text's brightness, or it is very busy: a subtle see-through backdrop behind the text.</summary>
    Backdrop = 2,
}
