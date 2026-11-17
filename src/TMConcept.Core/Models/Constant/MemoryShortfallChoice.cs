namespace TMConcept.Core.Models;

/// <summary>What the user chose when the memory a model needs is not free (<see cref="IMemoryShortfallHandler"/>).</summary>
public enum MemoryShortfallChoice
{
    /// <summary>Programs were closed: measure again after a short wait.</summary>
    Retry = 1,

    /// <summary>Give up: the video queue stops (the app stays open).</summary>
    Cancel = 2,
}
