using System.Globalization;
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

    /// <summary>
    /// A model the server listed with what its own API says (Ollama, LM Studio: name, images, thinking, JSON schema, size; a plain server
    /// gives only the id and maybe the context).
    /// </summary>
    public EndpointModelRowViewModel(ListedModel model, bool picked)
        : this((model ?? throw new ArgumentNullException(nameof(model))).Id, model.ContextTokens, picked)
    {
        _name = model.Name;
        Vision = model.Vision;
        JsonSchema = model.JsonSchema;
        CanThink = model.CanThink;
        SizeBytes = model.SizeBytes;
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
        CanThink = model.CanThink;
        Think = model.Think;
        SizeBytes = model.SizeBytes;
        Picked = true;
    }

    /// <summary>Model id (sent to the server).</summary>
    public string Id { get; }

    /// <summary>The model can think (Ollama): <see cref="Think"/> is offered.</summary>
    public bool CanThink { get; }

    /// <summary>Size on disk in bytes (0 = not said).</summary>
    public long SizeBytes { get; }

    /// <summary>The line shown: the id, the server's name for it when it differs, and its size on disk.</summary>
    public string Label => Id
        + (_name.Length > 0 && _name != Id ? " — " + _name : string.Empty)
        + (SizeBytes > 0 ? " · " + (SizeBytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " GB" : string.Empty);

    /// <summary>Let the model think before it answers (slower, often better; off by default).</summary>
    [ObservableProperty]
    public partial bool Think { get; set; }

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
        CanThink = CanThink,
        Think = CanThink && Think,
        SizeBytes = SizeBytes,
    };
}
