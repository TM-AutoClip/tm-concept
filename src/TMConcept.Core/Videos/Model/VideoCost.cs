namespace TMConcept.Core.Videos;

/// <summary>One line of a video's cost breakdown (concept, code, images, speech).</summary>
public sealed class VideoCost
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Video.</summary>
    public Guid VideoId { get; set; }

    /// <summary>Step code.</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>The run behind it.</summary>
    public Guid? RunId { get; set; }

    /// <summary>Cost (USD).</summary>
    public decimal Usd { get; set; }

    /// <summary>Exact or computed from unit prices.</summary>
    public bool Exact { get; set; }
}
