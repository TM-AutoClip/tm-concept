using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Endpoints;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A model of a local server while it is being added or edited: ticked to keep it, with its limits (the server's context window when it
/// reported one, otherwise <see cref="EndpointDefaults"/>; the answer limit from the context) and what it can do (images, JSON schema).
/// </summary>
public sealed partial class EndpointModelRowViewModel : ObservableObject
{
    // A saved model's display name (none for a listed or typed one): kept when the server is edited.
    private readonly string _name = string.Empty;

    /// <summary>A model the server listed (or the user typed), with defaults from <paramref name="contextTokens"/> (0 = not reported).</summary>
    public EndpointModelRowViewModel(string id, long contextTokens, bool picked)
    {
        Id = id;
        var context = contextTokens > 0 ? contextTokens : EndpointDefaults.ContextTokens;
        ContextTokens = context;

        // The context less room for the prompt (half of a small one): the video code answer needs tens of thousands of tokens.
        MaxOutputTokens = EndpointCodes.DefaultMaxOutput(context);
        Picked = picked;
    }

    /// <summary>A saved model of a server being edited, ticked, with the limits and flags the user gave it.</summary>
    public EndpointModelRowViewModel(EndpointModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Id = model.Id;
        _name = model.Name;
        ContextTokens = model.ContextTokens > 0 ? model.ContextTokens : EndpointDefaults.ContextTokens;
        MaxOutputTokens = model.MaxOutputTokens > 0 ? model.MaxOutputTokens : EndpointCodes.DefaultMaxOutput(model.ContextTokens);
        Vision = model.Vision;
        JsonSchema = model.JsonSchema;
        Picked = true;
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
        Name = _name,
        ContextTokens = ContextTokens is > 0 and var context ? (long)context : EndpointDefaults.ContextTokens,
        MaxOutputTokens = MaxOutputTokens is > 0 and var output ? (int)Math.Min(output, int.MaxValue) : EndpointDefaults.MaxOutputTokens,
        Vision = Vision,
        JsonSchema = JsonSchema,
    };
}
