namespace TMConcept.Desktop.ViewModels;

/// <summary>How an install request ended: everything is ready, or why not (canceled, failed, not available on this computer).</summary>
public sealed record InstallOutcome(bool Ready, string Reason);
