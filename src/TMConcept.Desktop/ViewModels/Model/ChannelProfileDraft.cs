namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The profile "Kanaldan esinlen" makes after the user's edits (0.1.53): always a new profile (never written over an existing one). The photo is
/// the channel's thumbnail in the look's temporary folder (null when not kept); the profile page copies it into the library on save.
/// </summary>
/// <param name="Name">Channel name of the new profile.</param>
/// <param name="Language">Its output language.</param>
/// <param name="Vertical">Vertical short videos (else horizontal).</param>
/// <param name="TargetSeconds">Target length.</param>
/// <param name="ConceptText">The channel content.</param>
/// <param name="PhotoFile">The channel photo to keep, or null.</param>
public sealed record ChannelProfileDraft(string Name, string Language, bool Vertical, int TargetSeconds, string ConceptText, string? PhotoFile);
