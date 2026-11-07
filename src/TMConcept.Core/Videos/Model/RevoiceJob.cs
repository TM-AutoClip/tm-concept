using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// A new version with a new voice: the version it starts from, the profile, one narration text per scene (edited, with narration markers)
/// and the voice (null = a silent version).
/// </summary>
public sealed record RevoiceJob(VersionSource Source, Profile Profile, IReadOnlyList<string> SceneTexts, SpeechSetup? Voice);
