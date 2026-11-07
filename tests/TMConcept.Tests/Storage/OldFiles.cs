namespace TMConcept.Tests.Storage;

/// <summary>
/// Database files written by the released versions themselves, with made-up values in every table (Storage/Files). Made with
/// Storage/Files/fixture-generator.cs.txt built against each version's own storage code: <see cref="Version1"/> by v0.1.2 (EF Core,
/// EnsureCreated), <see cref="Version2"/> by 0.1.5 (commit 649d06e, hand-written SQL, no version number), <see cref="Version3"/> by 0.1.6
/// (commit a3cd450, hand-written SQL, user_version 3). All are encrypted with <see cref="Key"/> (bytes 0…31).
/// </summary>
internal static class OldFiles
{
    public const string Version1 = "v1-0.1.2.db";

    public const string Version2 = "v2-0.1.5.db";

    public const string Version3 = "v3-0.1.6.db";

    /// <summary>The database key of every file.</summary>
    public static byte[] Key => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    /// <summary>A copy of <paramref name="name"/> at <paramref name="target"/> (the asset itself is never changed).</summary>
    public static string CopyTo(string name, string target)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Storage", "Files", name), target, overwrite: true);
        return target;
    }

    /// <summary>A harness whose database is a copy of <paramref name="name"/> with its key loaded (not opened yet).</summary>
    public static StorageHarness Harness(string name)
    {
        var harness = new StorageHarness();
        CopyTo(name, harness.Paths.DatabaseFile);
        harness.Key.Load(Key);
        return harness;
    }
}
