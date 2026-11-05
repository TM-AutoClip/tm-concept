using TMConcept.Core.Models;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A model in a picker (<see cref="Model"/> null = "no model chosen").</summary>
public sealed record ModelOption(CatalogModel? Model, string Label);
