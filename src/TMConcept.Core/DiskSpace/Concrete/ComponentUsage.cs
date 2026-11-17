using TMConcept.Core.Components;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.DiskSpace;

/// <summary>
/// <see cref="IComponentUsage"/>: every profile's saved step choices → the catalog model's <see cref="CatalogModel.Requires"/>, plus the
/// engine of the video code step (kept in its options, also before a code model is picked), plus the app's own needs
/// (<see cref="FixedNeeds"/>), closed over <see cref="IComponent.DependsOn"/> (an image model brings stable-diffusion.cpp, a Python voice its
/// environment, Python and uv). A step that is off needs nothing.
/// </summary>
public sealed class ComponentUsage(IProfileStore profiles, IStepModelStore steps, IModelCatalog catalog, ComponentPlanner planner) : IComponentUsage
{
    /// <summary>What the app itself needs whatever is chosen: ffmpeg (voice samples, the final video, playback).</summary>
    public static readonly IReadOnlyList<string> FixedNeeds = [ComponentCodes.Ffmpeg];

    /// <inheritdoc />
    public async Task<UsageMap> ComputeAsync(CancellationToken cancellationToken)
    {
        var direct = new Dictionary<string, List<DiskUse>>(StringComparer.Ordinal);
        foreach (var code in FixedNeeds)
        {
            Add(direct, code, new DiskUse(string.Empty, string.Empty));
        }

        foreach (var profile in await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
        {
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

                foreach (var dependency in planner.Find(next)?.DependsOn ?? [])
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
