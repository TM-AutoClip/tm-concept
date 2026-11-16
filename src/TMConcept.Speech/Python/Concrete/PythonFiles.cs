using System.Reflection;

namespace TMConcept.Speech.Python;

/// <summary>
/// The files that come with the app for the Python voices (embedded resources <c>TMConcept.Python.*</c>): the worker, its helpers and adapters,
/// F5's reference clip and the lock files. The scripts are written to the scripts folder before a worker starts (only when they differ, so a
/// new app version replaces them and an unchanged one never rewrites them).
/// </summary>
public static class PythonFiles
{
    private const string Prefix = "TMConcept.Python.";
    private const string LockPrefix = Prefix + "Locks.";

    /// <summary>The worker script.</summary>
    public const string Worker = "worker.py";

    /// <summary>The files written to the scripts folder (everything but the lock files).</summary>
    public static IReadOnlyList<string> Scripts { get; } = Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && !n.StartsWith(LockPrefix, StringComparison.Ordinal))
        .Select(n => n[Prefix.Length..]).Order(StringComparer.Ordinal).ToList();

    private static Assembly Assembly => typeof(PythonFiles).Assembly;

    /// <summary>A shipped file's bytes.</summary>
    public static byte[] Read(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(Prefix + name)
            ?? throw new FileNotFoundException("The app has no Python file " + name, name);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The lock file of <paramref name="environment"/> for a PyTorch build (<see cref="PythonPins"/>).</summary>
    public static string Lock(PythonEnvironment environment, string backend)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return System.Text.Encoding.UTF8.GetString(Read("Locks." + environment.Key + "-" + backend + ".txt"));
    }

    /// <summary>Whether the app has a lock file of <paramref name="environment"/> for <paramref name="backend"/>.</summary>
    public static bool HasLock(PythonEnvironment environment, string backend)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Assembly.GetManifestResourceInfo(LockPrefix + environment.Key + "-" + backend + ".txt") is not null;
    }

    /// <summary>Writes the scripts into <paramref name="folder"/> (each only when its content differs).</summary>
    public static async Task WriteScriptsAsync(string folder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in Scripts)
        {
            var bytes = Read(name);
            var target = Path.Combine(folder, name);
            if (File.Exists(target) && (await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false)).AsSpan().SequenceEqual(bytes))
            {
                continue;
            }

            await File.WriteAllBytesAsync(target, bytes, cancellationToken).ConfigureAwait(false);
        }
    }
}
