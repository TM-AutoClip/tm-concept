namespace TMConcept.Core.Videos;

/// <summary>
/// A produced video. Its title is copied, so the record stays meaningful after its concept is deleted. Files are in the library folder
/// (<see cref="Folder"/> is relative to it).
/// </summary>
public sealed class VideoRecord
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Concept (null when the concept was deleted later).</summary>
    public Guid? ConceptId { get; set; }

    /// <summary>Profile.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Title copy.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Folder relative to the library folder.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Video file name inside the folder.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Thumbnail file name inside the folder.</summary>
    public string ThumbnailName { get; set; } = string.Empty;

    /// <summary>Duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Width.</summary>
    public int Width { get; set; }

    /// <summary>Height.</summary>
    public int Height { get; set; }

    /// <summary>Frames per second.</summary>
    public int Fps { get; set; }

    /// <summary>File size.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Engine code.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>Engine version.</summary>
    public string EngineVersion { get; set; } = string.Empty;

    /// <summary>Model that wrote the code.</summary>
    public string CodeModel { get; set; } = string.Empty;

    /// <summary>Model that wrote the concept.</summary>
    public string ConceptModel { get; set; } = string.Empty;

    /// <summary>Speech engine and voice (empty = silent).</summary>
    public string SpeechModel { get; set; } = string.Empty;

    /// <summary>Image model (empty = none).</summary>
    public string ImageModel { get; set; } = string.Empty;

    /// <summary>Production settings as JSON.</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>Version of the code prompt.</summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>Total cost (USD).</summary>
    public decimal TotalUsd { get; set; }

    /// <summary>Whether every part of the cost is exact.</summary>
    public bool CostExact { get; set; }

    /// <summary>Production time in milliseconds.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Warnings and labels shown in the details (one per line), e.g. "silent" or the non-commercial voice label.</summary>
    public string Warnings { get; set; } = string.Empty;

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
