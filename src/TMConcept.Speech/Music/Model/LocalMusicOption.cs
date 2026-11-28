namespace TMConcept.Speech.Music;

/// <summary>
/// One argument of a model's run that its family's script reads (28 Nov 2026): what tells the models of a family apart at run time is data of
/// the model (<see cref="LocalMusicVariant.Options"/>: YuE2's memory budget and low-memory mode, SongGeneration's code version, YuE 1's stage
/// folders and exllamav2 run), never a comparison in the generator.
/// </summary>
/// <param name="Name">The argument (<c>--budget</c>).</param>
/// <param name="Value">Its value; empty for a switch (<c>--low-vram</c>).</param>
/// <param name="InFamilyFolder">Its value is a path inside the family's model folder (forward slashes), given to the script as a full path.</param>
public sealed record LocalMusicOption(string Name, string Value = "", bool InFamilyFolder = false);
