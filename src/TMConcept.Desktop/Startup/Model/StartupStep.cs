namespace TMConcept.Desktop.Startup;

/// <summary>
/// One step of the start: <paramref name="Run"/> runs on a pool thread (never on the interface thread) and may name a later stage it reaches
/// itself through the progress it gets (the vault step reports the database stage). <paramref name="Handle"/> (optional, on the caller's
/// thread): a failure it returns true for is taken over by it (the splash shows no error and nothing later runs).
/// </summary>
public sealed record StartupStep(StartupStage Stage, Func<IProgress<StartupStage>, Task> Run, Func<Exception, bool>? Handle = null);
