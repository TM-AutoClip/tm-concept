namespace TMConcept.Core.Documents;

/// <summary>
/// Which pages of a PDF document are read and kept (0.1.25): its pages, how many are ready (a text layer, or read by an engine), how many
/// of them an engine read, the pages still to read (a scan never read, or left out by a cancel or a stop) and the kept texts' characters.
/// </summary>
public sealed record DocumentPageState(int Pages, int Ready, int Recognized, IReadOnlyList<int> Missing, long Characters)
{
    /// <summary>Nothing is known (not a PDF, or added before 0.1.25).</summary>
    public static DocumentPageState Unknown { get; } = new(0, 0, 0, [], 0);

    /// <summary>Whether the pages are known.</summary>
    public bool Known => Pages > 0;
}
