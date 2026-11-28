namespace TMConcept.Core.DiskSpace;

/// <summary>
/// What the start-up sweep deletes for good without asking (0.1.37; 28 Nov 2026: whatever the earlier state), decided from path lists only (no
/// disk access): the old components folders that "Reinstall from scratch" of a version up to 0.1.56 left next to <c>tools</c>
/// (<c>tools.old-&lt;stamp&gt;</c>, <c>tools.failed-&lt;stamp&gt;</c>, <c>tools.aside-&lt;stamp&gt;</c>) with its marker file
/// (<c>tools.reinstalling</c>), the staging folders an install left (the shared downloads folder, <c>*.unpack</c>), the temporary folder of
/// each engine package (<c>tools/engines/&lt;name&gt;/tmp</c>), the temporary folders of "Kanaldan esinlen" and the work folders of the music
/// page's pieces (0.1.59).
/// </summary>
public interface ILeftoverSweep
{
    /// <summary>
    /// Of <paramref name="folders"/>, those that are an old components folder of <paramref name="tools"/>: a sibling of it whose name is exactly
    /// <c>&lt;tools name&gt;</c> + <c>.old-</c>, <c>.failed-</c> or <c>.aside-</c> + <c>yyyyMMdd-HHmmss</c>; <paramref name="tools"/> itself never.
    /// </summary>
    IReadOnlyList<string> OldComponentFolders(string tools, IEnumerable<string> folders);

    /// <summary>The marker file a "Reinstall from scratch" of a version up to 0.1.56 kept next to <paramref name="tools"/> (<c>tools.reinstalling</c>).</summary>
    string OldReinstallMarker(string tools);

    /// <summary>
    /// Of <paramref name="folders"/>, the staging folders an install left inside <paramref name="tools"/>: the shared downloads folder
    /// (<c>tools/downloads</c>, archives on their way, a <c>.part</c> of a download that did not finish) and every <c>*.unpack</c> folder (an
    /// archive being unpacked before it is moved into place). Only while no install runs (the caller's check).
    /// </summary>
    IReadOnlyList<string> Stagings(string tools, IEnumerable<string> folders);

    /// <summary>The temporary folder of each of <paramref name="engineFolders"/> that is a package folder right under <c>&lt;tools&gt;/engines</c>.</summary>
    IReadOnlyList<string> EngineTemps(string tools, IEnumerable<string> engineFolders);

    /// <summary>
    /// The temporary folders of "Kanaldan esinlen" (0.1.53) a closed app left: each of <paramref name="folders"/> right under
    /// <c>&lt;work&gt;/channel</c> that is not in <paramref name="active"/> (a look running now, or one whose preview is still open).
    /// </summary>
    IReadOnlyList<string> ChannelTemps(string work, IEnumerable<string> folders, IReadOnlyCollection<string> active);

    /// <summary>
    /// The work folders of "Müzik üret" a closed or stopped app left (0.1.59: a stopped YuE run left one behind): each of
    /// <paramref name="folders"/> right under <c>&lt;work&gt;/music-make</c> that is not in <paramref name="active"/> (a piece being made now).
    /// </summary>
    IReadOnlyList<string> MusicTemps(string work, IEnumerable<string> folders, IReadOnlyCollection<string> active);
}
