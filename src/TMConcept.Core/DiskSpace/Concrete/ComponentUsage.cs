using TMConcept.Core.Components;
using TMConcept.Core.Documents;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.DiskSpace;

/// <summary>
/// <see cref="IComponentUsage"/>: every profile's saved step choices → the catalog model's <see cref="CatalogModel.Requires"/>, plus the
/// engine of the video code step (kept in its options, also before a code model is picked), plus the app's own needs
/// (<see cref="FixedNeeds"/>), closed over <see cref="IComponent.DependsOn"/> (an image model brings stable-diffusion.cpp, a Python voice its
/// environment, Python and uv) and <see cref="IComponentUses.Uses"/> (HyperFrames and Revideo bring the shared browser). A step that is off
/// needs nothing. A profile with documents (0.1.21) needs the documents' meaning model (<paramref name="documentEmbedder"/>), so "Clean up
/// unused" does not take it while documents use it.
/// </summary>
public sealed class ComponentUsage(IProfileStore profiles, IStepModelStore steps, IModelCatalog catalog, ComponentPlanner planner,
    IDocumentStore? documents = null, IDocumentEmbedder? documentEmbedder = null) : IComponentUsage
{
    /// <summary>
    /// What the app itself needs whatever is chosen: ffmpeg (voice samples, the final video, playback) and the music library once it is there
    /// (0.1.45: templates choose it, and a template is not a profile's step; "Clean up unused" never takes it, it is removed on its own).
    /// </summary>
    public static readonly IReadOnlyList<string> FixedNeeds = [ComponentCodes.Ffmpeg, ComponentCodes.MusicLibrary];

    /// <inheritdoc />
    public async Task<UsageMap> ComputeAsync(CancellationToken cancellationToken)
    {
        var direct = new Dictionary<string, List<DiskUse>>(StringComparer.Ordinal);
        foreach (var code in FixedNeeds)
        {
            Add(direct, code, new DiskUse(string.Empty, string.Empty));
        }

        HashSet<Guid> withDocuments = documents is null || documentEmbedder is null
            ? []
            : (await documents.ListAllAsync(cancellationToken).ConfigureAwait(false)).Select(d => d.ProfileId).ToHashSet();
        foreach (var profile in await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (withDocuments.Contains(profile.Id))
            {
                Add(direct, documentEmbedder!.ComponentCode, new DiskUse(profile.Name, "documents"));
            }

            foreach (var choice in await steps.ListAsync(profile.Id, cancellationToken).ConfigureAwait(false))
            {
                foreach (var code in CodesOf(choice))
                {
                    Add(direct, code, new DiskUse(profile.Name, choice.Step));
                }
            }
        }

        // Each dependency is needed by whoever needs the component that depends on it.
        var closed = new Dictionary<string, List<DiskUse>>(StringComparer.Ordinal);
        foreach (var (code, uses) in direct)
        {
            var pending = new Stack<string>([code]);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryPop(out var next))
            {
                if (!seen.Add(next))
                {
                    continue;
                }

                foreach (var use in uses)
                {
                    Add(closed, next, use);
                }

                var component = planner.Find(next);
                foreach (var dependency in (component?.DependsOn ?? []).Concat((component as IComponentUses)?.Uses ?? []))
                {
                    pending.Push(dependency);
                }
            }
        }

        return new UsageMap(closed.ToDictionary(p => p.Key, p => (IReadOnlyList<DiskUse>)p.Value, StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CodesOf(StepModelChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var codes = new List<string>();
        if (!StepModelResolver.IsOff(choice) && catalog.Find(choice.Provider, choice.Model) is { } model)
        {
            codes.AddRange(model.Requires);
        }

        if (choice.Step == PipelineSteps.VideoCode)
        {
            // The same code as Engines' EngineComponent.CodeOf (a test holds them together).
            codes.Add(ComponentCodes.EnginePrefix + StepModelResolver.Options(choice).Engine.ToString().ToLowerInvariant());
        }

        return codes;
    }

    private static void Add(Dictionary<string, List<DiskUse>> map, string code, DiskUse use)
    {
        if (!map.TryGetValue(code, out var list))
        {
            map[code] = list = [];
        }

        if (!list.Contains(use))
        {
            list.Add(use);
        }
    }
}
