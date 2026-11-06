using TMConcept.Core.Models;

namespace TMConcept.Desktop.ViewModels;

/// <summary>An entry of a step's picker: use the general setting, none / off, or a model.</summary>
public sealed record StepOption(StepOptionKind Kind, CatalogModel? Model, string Label);
