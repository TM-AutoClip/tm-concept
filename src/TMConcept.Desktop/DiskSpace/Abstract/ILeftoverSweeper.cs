namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The start-up sweep (0.1.37), run in the background after the main window opened: deletes for good, without asking (28 Nov 2026), the old
/// components folders and the marker file a "Reinstall from scratch" of a version up to 0.1.56 left next to <c>tools</c> (<c>tools.old-*</c>,
/// <c>tools.failed-*</c>, <c>tools.aside-*</c>, <c>tools.reinstalling</c>), the staging folders an install left (<c>tools/downloads</c>,
/// <c>*.unpack</c>), the engine packages' leftover <c>tmp</c> folders and the closed looks of "Kanaldan esinlen". Nothing while a component
/// install runs; a folder with a file in use stays for the next start. What it deleted goes to the log and, when anything, to the activity panel.
/// </summary>
public interface ILeftoverSweeper
{
    /// <summary>Sweeps; returns the bytes freed. Never throws for a folder that could not be deleted.</summary>
    Task<long> SweepAsync(CancellationToken cancellationToken);
}
