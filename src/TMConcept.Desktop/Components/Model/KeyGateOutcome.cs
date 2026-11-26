namespace TMConcept.Desktop.Components;

/// <summary>What the key gate of <see cref="IModelStartGate"/> came to.</summary>
/// <param name="MissingProvider">The provider still without a working key (the user closed the key window); null = every provider has one.</param>
/// <param name="Untested">Providers whose new key was kept untested (no free test exists: fal); it is checked at its first use.</param>
public sealed record KeyGateOutcome(string? MissingProvider, IReadOnlyList<string> Untested);
