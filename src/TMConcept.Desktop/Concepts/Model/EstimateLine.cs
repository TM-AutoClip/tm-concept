namespace TMConcept.Desktop.Concepts;

/// <summary>
/// An estimate as the page shows it: <see cref="Tiny"/> next to the button ("≈ 0.02 $–0.05 $", empty without a model or anything to run)
/// and <see cref="Detail"/> as its tooltip (the model, the count, the tokens, what the meaning check embeds first).
/// </summary>
public sealed record EstimateLine(string Tiny, string Detail);
