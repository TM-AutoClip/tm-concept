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

    /// <summary>
    /// The moment the lock file of <paramref name="environment"/> for <paramref name="backend"/> was made against: the <c>--exclude-newer</c> of
    /// its header (0.1.54). The install gives uv the same moment, so a lock made later than another one (yazgı-tts: 20 Nov 2026) is never cut
    /// off by an older date (0.1.35–0.1.53 gave every lock 1 Nov 2026, and uv found no filelock 4.2.12 uploaded on 18 Nov). A lock without
    /// one falls back to <see cref="PythonPins.ExcludeNewer"/>.
    /// </summary>
    public static string ExcludeNewer(PythonEnvironment environment, string backend) => ExcludeNewerOf(Lock(environment, backend));

    /// <summary>The <c>--exclude-newer</c> moment named in a lock file's header comments; <see cref="PythonPins.ExcludeNewer"/> when none is.</summary>
    public static string ExcludeNewerOf(string lockText)
    {
        ArgumentNullException.ThrowIfNull(lockText);
        foreach (var line in lockText.Split('\n').Select(l => l.Trim()).TakeWhile(l => l.Length == 0 || l.StartsWith('#')))
        {
            var at = line.IndexOf("--exclude-newer", StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var value = line[(at + "--exclude-newer".Length)..].Trim().Split(' ', 2)[0].Trim();
            if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out _))
            {
                return value;
            }
        }

        return PythonPins.ExcludeNewer;
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
