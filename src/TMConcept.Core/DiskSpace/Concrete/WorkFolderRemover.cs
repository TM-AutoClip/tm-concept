using Microsoft.Extensions.Logging;

namespace TMConcept.Core.DiskSpace;

/// <inheritdoc cref="IWorkFolderRemover" />
/// <remarks><see cref="Attempts"/> tries, <see cref="Pause"/> apart (on <see cref="TimeProvider"/>: tests move a fake clock).</remarks>
public sealed class WorkFolderRemover(TimeProvider clock, ILogger<WorkFolderRemover> logger) : IWorkFolderRemover
{
    /// <summary>How many times the delete is tried.</summary>
    public const int Attempts = 5;

    /// <summary>The pause between two tries.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= Attempts)
                {
                    logger.LogWarning(ex, "A work folder could not be removed after {Attempts} tries; the start-up sweep removes it later", Attempts);
                    return false;
                }

                logger.LogInformation("A work folder is still in use ({Reason}); trying again in {Seconds:0.#} s", ex.Message, Pause.TotalSeconds);
            }

            await Task.Delay(Pause, clock, cancellationToken).ConfigureAwait(false);
        }
    }
}
