namespace TMConcept.Core.Styles;

/// <summary>Where and when the closing card is drawn (<see cref="StyleOverlay"/>), in video pixels and seconds.</summary>
/// <param name="StartSeconds">The card fades in here.</param>
/// <param name="EndSeconds">The card stays until here (the end of the video).</param>
/// <param name="Photo">The channel photo is shown (the profile has one).</param>
/// <param name="PhotoX">Left edge of the round photo.</param>
/// <param name="PhotoY">Top edge of the round photo.</param>
/// <param name="PhotoSize">Width and height of the round photo (even).</param>
public sealed record EndCardLayout(double StartSeconds, double EndSeconds, bool Photo, int PhotoX, int PhotoY, int PhotoSize);
