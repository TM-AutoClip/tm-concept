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
    /// (<paramref name="reinstalling"/>); <paramref name="tools"/> itself never.
    /// </summary>
    IReadOnlyList<string> OldComponentFolders(string tools, IEnumerable<string> folders, bool reinstalling);

    /// <summary>The temporary folder of each of <paramref name="engineFolders"/> that is a package folder right under <c>&lt;tools&gt;/engines</c>.</summary>
    IReadOnlyList<string> EngineTemps(string tools, IEnumerable<string> engineFolders);
}
