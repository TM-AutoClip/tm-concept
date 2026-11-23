using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Startup;

/// <summary>The splash window of the start, what it shows (<paramref name="Screen"/>) and the start steps its Cancel button ends.</summary>
public sealed record StartupSplash(SplashWindow Window, IStartupScreen Screen, IStartupSequence Sequence);
