namespace TMConcept.Desktop.Components;

/// <summary>What <see cref="IModelStartGate.PrepareAsync"/> came to: ready to run, or why not (in the app's language).</summary>
/// <param name="Ready">The models can run now.</param>
/// <param name="Reason">Why not (empty when ready).</param>
public sealed record ModelStartOutcome(bool Ready, string Reason);
