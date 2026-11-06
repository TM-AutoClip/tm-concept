namespace TMConcept.Desktop.ViewModels;

/// <summary>What an entry of a step's picker means.</summary>
public enum StepOptionKind
{
    /// <summary>No model: not chosen (a required step) or off (an optional step).</summary>
    None = 1,

    /// <summary>A model of the catalog (online or local).</summary>
    Model = 2,

    /// <summary>Images step: no image model, the profile's sample images are used.</summary>
    Samples = 3,
}
