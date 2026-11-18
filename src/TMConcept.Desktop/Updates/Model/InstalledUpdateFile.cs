namespace TMConcept.Desktop.Updates;

/// <summary>One package file of an installed-edition update.</summary>
/// <param name="FileName">Its file name on the release (<c>TMConcept-0.1.27-win-x64-delta.nupkg</c>).</param>
/// <param name="Version">The version whose release carries it.</param>
/// <param name="Sha256">Its SHA-256 as Velopack's release index gives it.</param>
/// <param name="Size">Its size in bytes.</param>
public sealed record InstalledUpdateFile(string FileName, string Version, string Sha256, long Size);
