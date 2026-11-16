namespace TMConcept.Engines.Images;

/// <summary>What <see cref="ImageMemoryPlanner"/> decided for one run: the level, sd-cli's memory options for it, and why it was refused.</summary>
/// <param name="Level">The level.</param>
/// <param name="Flags">sd-cli's memory options (flash attention is not here: the engine always passes it).</param>
/// <param name="ReserveGib">The graphics memory kept free for the system and other programs (<c>--max-vram -n</c>), in GiB.</param>
/// <param name="Reason">Why it was refused (empty otherwise).</param>
public sealed record ImageMemoryPlan(ImageMemoryLevel Level, IReadOnlyList<string> Flags, int ReserveGib, string Reason);
