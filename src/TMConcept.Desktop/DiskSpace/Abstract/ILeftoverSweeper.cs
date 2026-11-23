namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The start-up sweep (0.1.37), run in the background after the main window opened: deletes for good the old components folders a cut-short
/// "Reinstall from scratch" left next to <c>tools</c> (<c>tools.old-*</c>, <c>tools.failed-*</c>) and the engine packages' leftover
/// <c>tmp</c> folders. Nothing while a component install or reinstall runs; the old folders not at the first start after a reinstall was cut
/// short (they may hold the components that worked); a folder with a file in use stays for the next start. What it freed goes to the log
/// and, when anything, to the activity panel.
/// </summary>
public interface ILeftoverSweeper
{
    /// <summary>Sweeps; returns the bytes freed. Never throws for a folder that could not be deleted.</summary>
    Task<long> SweepAsync(CancellationToken cancellationToken);
}
