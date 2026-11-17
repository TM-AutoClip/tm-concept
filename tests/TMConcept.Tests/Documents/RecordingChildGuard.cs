using System.Diagnostics;
using TMConcept.Core.Processes;

namespace TMConcept.Tests.Documents;

/// <summary>A child process guard that only records which processes it was handed and which of those were let go again.</summary>
internal sealed class RecordingChildGuard : IChildProcessGuard
{
    public List<int> Adopted { get; } = [];

    public List<int> Released { get; } = [];

    public IDisposable Adopt(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var id = process.Id;
        lock (Adopted)
        {
            Adopted.Add(id);
        }

        return new Release(() =>
        {
            lock (Adopted)
            {
                Released.Add(id);
            }
        });
    }

    /// <summary>Runs its action when disposed.</summary>
    private sealed class Release(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
