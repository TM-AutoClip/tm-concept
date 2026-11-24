using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// "Fix" of a made version (F16, 0.1.49): the version it starts from, the profile, the notes per scene, the model of the one fix call, the voice
/// of the scenes whose narration changes (null = they are silent; a local voice costs nothing) and the image model of the scenes whose picture
/// changes (null = the pictures are kept).
/// </summary>
public sealed record FixJob(VersionSource Source, Profile Profile, IReadOnlyList<SceneNote> Notes, ResolvedModel CodeModel, SpeechSetup? Voice,
    ResolvedModel? ImageModel);
