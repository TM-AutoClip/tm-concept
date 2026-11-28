namespace TMConcept.Core.Components;

/// <summary>
/// The journal of component installs that have begun and not yet been verified as finished (28 Nov 2026, the developer: "yarıda kapattı, son
/// yarım kalanı siler, onu tekrar indirerek devam eder"). The components of an install are written here before any old file of theirs is
/// deleted and before a download starts, and each one is taken out only after its install ended and it is ready. It is kept outside the
/// components folder, so it outlives the app closing, a crash, a power cut and "Reinstall from scratch" (which deletes the whole components
/// folder first): at the next start the files of each unfinished component are deleted for good and it is downloaded again; the finished
/// ones are kept.
/// </summary>
public interface IInstallJournal
{
    /// <summary>The codes of the components whose install began and was not verified as finished, in the order they were written.</summary>
    IReadOnlyList<string> Unfinished();

    /// <summary>Writes <paramref name="codes"/> as begun (those already written stay where they are).</summary>
    void Begin(IEnumerable<string> codes);

    /// <summary>Takes <paramref name="code"/> out: its install ended and it is ready, or the user chose to install it later.</summary>
    void Finished(string code);
}
