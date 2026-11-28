namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// How an install request ended: everything is ready, or why not (canceled, failed, not available on this computer). <paramref name="Declined"/>:
/// the user said no to the downloads (nothing was installed or deleted by this request).
/// </summary>
public sealed record InstallOutcome(bool Ready, string Reason, bool Declined = false);
