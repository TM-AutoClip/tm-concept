namespace TMConcept.Core.Tools;

/// <summary>
/// One pinned file inside a published zip (0.1.27: a NuGet package of nuget.org, whose packages are zips): only the bytes of that entry are
/// fetched (an HTTP Range request of <paramref name="CompressedBytes"/> bytes from <paramref name="DataOffset"/>, the entry's data after its
/// local header), inflated when <paramref name="Deflated"/>, and the SHA-256 of the unpacked file is checked against <paramref name="Sha256"/>
/// before it is moved into place, so the integrity is that of a whole-file pin without downloading a 200 MB package for one file.
/// </summary>
/// <param name="Tool">What it belongs to.</param>
/// <param name="Version">The package version.</param>
/// <param name="Url">The package's address.</param>
/// <param name="DataOffset">Where the entry's (compressed) data starts in the package.</param>
/// <param name="CompressedBytes">How many bytes it has in the package.</param>
/// <param name="Deflated">Whether it is stored with deflate (false: stored as it is).</param>
/// <param name="Sha256">The SHA-256 of the unpacked file.</param>
/// <param name="FileName">The file's name on disk.</param>
/// <param name="Bytes">The unpacked file's size.</param>
public sealed record ZipEntryArchive(ToolKind Tool, string Version, Uri Url, long DataOffset, long CompressedBytes, bool Deflated, string Sha256,
    string FileName, long Bytes);
