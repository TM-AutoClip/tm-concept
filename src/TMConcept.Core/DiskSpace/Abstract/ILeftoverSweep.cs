namespace TMConcept.Core.DiskSpace;

/// <summary>
/// What the start-up sweep deletes (0.1.37), decided from folder lists only (no disk access): the old components folders that "Reinstall from
/// scratch" leaves next to <c>tools</c> after a crash (<c>tools.old-&lt;stamp&gt;</c>, <c>tools.failed-&lt;stamp&gt;</c>) and the temporary
/// folder of each engine package (<c>tools/engines/&lt;name&gt;/tmp</c>).
/// </summary>
public interface ILeftoverSweep
{
    /// <summary>
    /// Of <paramref name="folders"/>, those that are an old components folder of <paramref name="tools"/>: a sibling of it whose name is exactly
    /// <c>&lt;tools name&gt;.old-yyyyMMdd-HHmmss</c> or <c>&lt;tools name&gt;.failed-yyyyMMdd-HHmmss</c>. None while a reinstall runs
    /// (<paramref name="reinstalling"/>) and none while <paramref name="tools"/> is not there (<paramref name="toolsPresent"/> false: an old
    /// folder may then be the only copy of the components); <paramref name="tools"/> itself never.
    /// </summary>
    IReadOnlyList<string> OldComponentFolders(string tools, IEnumerable<string> folders, bool reinstalling, bool toolsPresent);

    /// <summary>The temporary folder of each of <paramref name="engineFolders"/> that is a package folder right under <c>&lt;tools&gt;/engines</c>.</summary>
    IReadOnlyList<string> EngineTemps(string tools, IEnumerable<string> engineFolders);

    /// <summary>
    /// The temporary folders of "Kanaldan esinlen" (0.1.53) a closed app left: each of <paramref name="folders"/> right under
    /// <c>&lt;work&gt;/channel</c> that is not in <paramref name="active"/> (a look running now, or one whose preview is still open).
    /// </summary>
    IReadOnlyList<string> ChannelTemps(string work, IEnumerable<string> folders, IReadOnlyCollection<string> active);
}
