using TMConcept.Core.Models;

namespace TMConcept.Desktop.ViewModels;

/// <summary>An entry of a step's picker: none / off, the sample images, or a model.</summary>
public sealed record StepOption(StepOptionKind Kind, CatalogModel? Model, string Label);
