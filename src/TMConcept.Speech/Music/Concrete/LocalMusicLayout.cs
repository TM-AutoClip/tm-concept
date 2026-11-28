using TMConcept.Core.Paths;

namespace TMConcept.Speech.Music;

/// <summary>
/// Where the local music families live (28 Nov 2026): everything under <c>&lt;tools&gt;/music-models</c>. <c>&lt;family&gt;/&lt;model&gt;/</c> a
/// model's own files with its marker, <c>&lt;family&gt;/&lt;shared&gt;/</c> files several models of a family share (MAGNeT's T5),
/// <c>&lt;family&gt;/source/&lt;key&gt;/</c> the source a family runs from (MAGNeT: audiocraft) with its marker, <c>&lt;family&gt;/downloads/&lt;key&gt;/</c>
/// its archive while it is unpacked (two families may share a folder, <see cref="LocalMusicFamily.Folder"/>), <c>home/</c> the folder the libraries write to. The environments are the Python ones
/// (<c>&lt;tools&gt;/speech-py/env-&lt;key&gt;</c>), the scripts the app's Python files (<c>&lt;tools&gt;/speech-py/scripts</c>).
/// </summary>
public sealed class LocalMusicLayout(IAppPaths paths)
{
    /// <summary>The marker of a finished install (holds what was installed).</summary>
    public const string Marker = ".tmc-installed";

    /// <summary>The root.</summary>
    public string Root => Path.Combine(paths.ToolsDirectory, "music-models");

    /// <summary>A family's model folder (the files' paths are relative to it).</summary>
    public string FamilyFolder(LocalMusicFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return Path.Combine(Root, family.FolderName);
    }

    /// <summary>A model's own folder.</summary>
    public string ModelFolder(LocalMusicFamily family, LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return Path.Combine(FamilyFolder(family), variant.Folder);
    }

    /// <summary>The folder of the files a model shares with others of its family; null when it shares none.</summary>
    public string? SharedFolder(LocalMusicFamily family, LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return variant.SharedFolder.Length == 0 ? null : Path.Combine(FamilyFolder(family), variant.SharedFolder);
    }

    /// <summary>
    /// The folders of the files <paramref name="variant"/> keeps outside its own folder (each top folder of its files' paths that is not its own:
    /// MAGNeT's <c>t5-base</c>, DiffRhythm's and YuE 1's <c>shared</c>, SongGeneration's <c>runtime</c>, YuE2's <c>yue2-3b</c>).
    /// </summary>
    public IReadOnlyList<string> OutsideFolders(LocalMusicFamily family, LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return variant.Files.Select(f => f.ExecutablePath.Split('/')[0]).Where(top => top != variant.Folder).Distinct(StringComparer.Ordinal)
            .Select(top => Path.Combine(FamilyFolder(family), top)).ToList();
    }

    /// <summary>The arguments of <paramref name="variant"/>'s own options (<see cref="LocalMusicVariant.Options"/>), a path inside the family's folder made full.</summary>
    public IReadOnlyList<string> OptionArguments(LocalMusicFamily family, LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return variant.Options.SelectMany(o => o.Value.Length == 0
            ? new[] { o.Name }
            : [o.Name, o.InFamilyFolder ? Path.Combine(FamilyFolder(family), o.Value.Replace('/', Path.DirectorySeparatorChar)) : o.Value]).ToList();
    }

    /// <summary>A model's marker.</summary>
    public string ModelMarker(LocalMusicFamily family, LocalMusicVariant variant) => Path.Combine(ModelFolder(family, variant), Marker);

    /// <summary>The full path of one of a family's files (<see cref="Core.Tools.ToolArchive.ExecutablePath"/> is relative to its folder).</summary>
    public string FileOf(LocalMusicFamily family, Core.Tools.ToolArchive file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Path.Combine(FamilyFolder(family), file.ExecutablePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>The source a family runs from (its own, also when it shares its folder with another family: DiffRhythm 1.0 and 1.2).</summary>
    public string SourceFolder(LocalMusicFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return Path.Combine(FamilyFolder(family), "source", family.Key);
    }

    /// <summary>The source's marker.</summary>
    public string SourceMarker(LocalMusicFamily family) => Path.Combine(SourceFolder(family), Marker);

    /// <summary>A family's archive while it is unpacked.</summary>
    public string Downloads(LocalMusicFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return Path.Combine(FamilyFolder(family), "downloads", family.Key);
    }

    /// <summary>The folder the libraries write their settings and caches to (instead of the user's home).</summary>
    public string Home => Path.Combine(Root, "home");
}
