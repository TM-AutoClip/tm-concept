namespace TMConcept.Core.Models;

/// <summary>What a <see cref="MemoryNotice"/> tells the user.</summary>
public enum MemoryNoticeKind
{
    /// <summary>A model was just unloaded: waiting a moment for the driver to give its memory back.</summary>
    Settling = 1,

    /// <summary>The free memory is being measured (<see cref="MemoryNotice.Step"/> of <see cref="MemoryNotice.Of"/>).</summary>
    Measuring = 2,

    /// <summary>Not enough after the readings: the user is asked (Retry / Cancel).</summary>
    Asking = 3,

    /// <summary>The user chose Retry: measuring again after the fixed wait.</summary>
    Retrying = 4,

    /// <summary>Enough memory now, after waiting.</summary>
    Enough = 5,

    /// <summary>The model did not fit on the card: main memory or the disk is used (<see cref="MemoryNotice.Placement"/>), slower.</summary>
    Placed = 6,

    /// <summary>The graphics card build failed and the same model goes on with the processor build: much slower.</summary>
    ProcessorFallback = 7,

    /// <summary>Not enough memory and nobody is asked (night mode): this video fails.</summary>
    Failed = 8,

    /// <summary>The user chose Cancel: the queue stops.</summary>
    Canceled = 9,
}
