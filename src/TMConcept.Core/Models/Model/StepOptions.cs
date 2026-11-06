using System.Text.Json.Serialization;
using TMConcept.Core.Engines;

namespace TMConcept.Core.Models;

/// <summary>Options of a step's model (stored as JSON in the step choice).</summary>
public sealed class StepOptions
{
    /// <summary>Effort level (empty = "Automatic": no effort parameter is sent, the provider's default applies).</summary>
    public string Effort { get; set; } = string.Empty;

    /// <summary>Most output tokens (0 = the step's default).</summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>The video engine (video code step).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EngineKind>))]
    public EngineKind Engine { get; set; } = EngineKind.HyperFrames;

    /// <summary>The voice of the narration step (empty = the engine's first voice).</summary>
    public string Voice { get; set; } = string.Empty;

    /// <summary>Speech speed of the narration step (1 = normal).</summary>
    public double Speed { get; set; } = 1.0;
}
