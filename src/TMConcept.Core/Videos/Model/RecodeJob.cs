using TMConcept.Core.Engines;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Videos;

/// <summary>A new version for another engine: the version it starts from, the profile, the engine and the model that writes its code (paid).</summary>
public sealed record RecodeJob(VersionSource Source, Profile Profile, IVideoEngine Engine, ResolvedModel CodeModel);
