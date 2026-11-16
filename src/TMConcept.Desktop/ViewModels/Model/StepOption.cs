using TMConcept.Core.Models;

namespace TMConcept.Desktop.ViewModels;

/// <summary>An entry of a step's picker: none / off, the sample images, a model, or a saved model that is not offered now.</summary>
/// <param name="Kind">What it means.</param>
/// <param name="Model">The model (null for none / off, samples and an unavailable one).</param>
/// <param name="Label">What the picker shows.</param>
/// <param name="Kept">The saved choice an <see cref="StepOptionKind.Unavailable"/> entry stands for (saved back unchanged).</param>
public sealed record StepOption(StepOptionKind Kind, CatalogModel? Model, string Label, StepModelChoice? Kept = null);
