using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Endpoints;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A model of a local server while it is being added: ticked to keep it, with its limits (the server's context window when it reported one,
/// otherwise <see cref="EndpointDefaults"/>) and what it can do (images, JSON schema).
/// </summary>
public sealed partial class EndpointModelRowViewModel : ObservableObject
{
    /// <summary>A model the server listed (or the user typed), with defaults from <paramref name="contextTokens"/> (0 = not reported).</summary>
    public EndpointModelRowViewModel(string id, long contextTokens, bool picked)
    {
        Id = id;
        var context = contextTokens > 0 ? contextTokens : EndpointDefaults.ContextTokens;
        ContextTokens = context;

        // Half of a small context at most, so the prompt still fits next to the answer.
        MaxOutputTokens = Math.Min(EndpointDefaults.MaxOutputTokens, Math.Max(256, context / 2));
        Picked = picked;
    }

    /// <summary>Model id (sent to the server).</summary>
    public string Id { get; }

    /// <summary>Kept when the endpoint is saved.</summary>
    [ObservableProperty]
    public partial bool Picked { get; set; }

    /// <summary>Context window in tokens.</summary>
    [ObservableProperty]
    public partial decimal? ContextTokens { get; set; }

    /// <summary>Largest answer in tokens.</summary>
    [ObservableProperty]
    public partial decimal? MaxOutputTokens { get; set; }

    /// <summary>Reads images.</summary>
    [ObservableProperty]
    public partial bool Vision { get; set; }

    /// <summary>The server accepts a JSON schema for it.</summary>
    [ObservableProperty]
    public partial bool JsonSchema { get; set; }

    /// <summary>The model as it is saved.</summary>
    public EndpointModel ToModel() => new()
    {
        Id = Id,
        ContextTokens = ContextTokens is > 0 and var context ? (long)context : EndpointDefaults.ContextTokens,
        MaxOutputTokens = MaxOutputTokens is > 0 and var output ? (int)Math.Min(output, int.MaxValue) : EndpointDefaults.MaxOutputTokens,
        Vision = Vision,
        JsonSchema = JsonSchema,
    };
}
