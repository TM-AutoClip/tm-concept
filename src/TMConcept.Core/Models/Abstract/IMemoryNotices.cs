namespace TMConcept.Core.Models;

/// <summary>
/// Tells the user every step of a memory check and where a model's weights went (<see cref="MemoryNotice"/>): the desktop turns each into a
/// line of the activity panel in the user's language (a slower placement and the processor build in yellow, a failure in red). Never throws.
/// </summary>
public interface IMemoryNotices
{
    /// <summary>Reports one step.</summary>
    void Report(MemoryNotice notice);
}
