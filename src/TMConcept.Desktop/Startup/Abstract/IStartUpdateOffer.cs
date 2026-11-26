using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// The start's offer of a newer version (0.1.55): it comes BEFORE the components question of a version change, so nobody installs
/// components for an old version first (0.1.53 → 0.1.54 asked for the components of 0.1.53 and only then offered 0.1.54). The start waits
/// for the look a few seconds at most (<see cref="UpdateRules.StartWait"/>): no internet or a slow GitHub never holds it up, and an answer
/// that comes later is offered after the start's questions, as before.
/// </summary>
public interface IStartUpdateOffer
{
    /// <summary>
    /// Waits at most <paramref name="bound"/> for <paramref name="check"/>; when it answered with a newer version, offers it at once.
    /// <see cref="StartUpdateOutcome.Pending"/> when it had not answered (the caller offers it later with <see cref="OfferAsync"/>).
    /// </summary>
    Task<StartUpdateOutcome> OfferFirstAsync(Task<UpdateOffer?> check, TimeSpan bound);

    /// <summary>Waits for <paramref name="check"/> and offers what it found; a failed look is only logged.</summary>
    Task<StartUpdateOutcome> OfferAsync(Task<UpdateOffer?> check);
}
