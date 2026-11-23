using Microsoft.Extensions.Logging;

namespace TMConcept.Core.Diagnostics;

/// <summary>
/// Measures how long the steps of the start and of the page changes take and writes one line per step to the log, always in the same
/// form: <c>&lt;step&gt; &lt;ms&gt;</c> (for example <c>startup.services 412</c>, <c>page.concepts.activate 88</c>). The lines stay in the log
/// for good: later work compares them before and after a change. Lines measured before the log exists (the first steps of the start) are
/// kept and written when <see cref="Attach"/> gives the logger. Database connections are counted, not written one by one: a step that asks
/// for it writes how many were opened during it and how long opening them took in total.
/// </summary>
public interface IStepTimer
{
    /// <summary>Time since this timer was created (for the app's timer: the start of <c>Main</c>).</summary>
    TimeSpan SinceOrigin { get; }

    /// <summary>Every database connection opened so far, and the time spent opening them.</summary>
    ConnectionTotals Connections { get; }

    /// <summary>
    /// Measures a step until the returned object is disposed (steps may be nested: an outer step's time includes its inner steps);
    /// <paramref name="countConnections"/> adds the database connections opened meanwhile to its line.
    /// </summary>
    IDisposable Step(string name, bool countConnections = false);

    /// <summary>Starts a step that ends in another method (<see cref="End"/>); starting it again restarts it.</summary>
    void Begin(string name);

    /// <summary>Ends a step <see cref="Begin"/> started and writes its line; nothing when it was not started.</summary>
    void End(string name);

    /// <summary>Writes a step measured elsewhere.</summary>
    void Record(string name, TimeSpan elapsed, ConnectionTotals? connections = null);

    /// <summary>Writes the time since the timer's origin under <paramref name="name"/> (a point of the start, not a step).</summary>
    void Mark(string name);

    /// <summary>Counts one opened database connection and how long opening it took (no line is written for it).</summary>
    void CountConnection(TimeSpan opening);

    /// <summary>Total time of every line written under <paramref name="name"/> (a step that runs again adds up).</summary>
    TimeSpan Total(string name);

    /// <summary>Gives the log: the lines kept so far are written now, every later line at once.</summary>
    void Attach(ILogger logger);
}
