namespace TMConcept.Desktop.Startup;

/// <summary>
/// <see cref="IStartupSequence"/> (0.1.38, the developer: "a program shows a splash with its logo at once when it is double-clicked, with an
/// optional Cancel that ends the process"). Each step runs on a pool thread; the caller's thread (the interface thread) only waits for it.
/// </summary>
public sealed class StartupSequence(IStartupExit exit) : IStartupSequence
{
    private int _canceled;

    /// <inheritdoc />
    public bool Failed { get; private set; }

    /// <inheritdoc />
    public async Task<StartupOutcome> RunAsync(IReadOnlyList<StartupStep> steps, IStartupScreen screen)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(screen);
        foreach (var step in steps)
        {
            if (Volatile.Read(ref _canceled) != 0)
            {
                return StartupOutcome.Canceled;
            }

            screen.Report(step.Stage);
            try
            {
                await Task.Run(() => step.Run(screen)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (Volatile.Read(ref _canceled) != 0)
                {
                    return StartupOutcome.Canceled;
                }

                if (step.Handle?.Invoke(ex) == true)
                {
                    return StartupOutcome.Redirected;
                }

                Failed = true;
                screen.ShowError(step.Stage, ex.Message);
                return StartupOutcome.Failed;
            }
        }

        return Volatile.Read(ref _canceled) != 0 ? StartupOutcome.Canceled : StartupOutcome.Completed;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        if (Interlocked.Exchange(ref _canceled, 1) != 0)
        {
            return;
        }

        exit.KillChildren();
        exit.ReleaseLock();
        exit.End(Failed ? 1 : 0);
    }
}
