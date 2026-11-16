using TMConcept.Core.Models;

namespace TMConcept.Desktop.ViewModels;

/// <summary>An entry of a step's picker: none / off, the sample images, a model, or a saved model that is not offered now.</summary>
/// <param name="Kind">What it means.</param>
/// <param name="Model">The model (null for none / off, samples and an unavailable one).</param>
/// <param name="Label">What the picker shows.</param>
/// <param name="Kept">The saved choice an <see cref="StepOptionKind.Unavailable"/> entry stands for (saved back unchanged).</param>
public sealed record StepOption(StepOptionKind Kind, CatalogModel? Model, string Label, StepModelChoice? Kept = null)
{
    /// <summary>
    /// Green: a local image model of this card's memory class or a lower one (0.1.17: a card sees its own class and every lower one).
    /// </summary>
    public bool IsOk { get; init; }

    /// <summary>Yellow: it may run but slowly or unknown (the processor build, a card whose memory could not be read), or its license has conditions.</summary>
    public bool IsWarning { get; init; }

    /// <summary>Red: a local model of a higher memory class than this card's (it cannot run here), or a non-commercial license.</summary>
    public bool IsError { get; init; }
}
