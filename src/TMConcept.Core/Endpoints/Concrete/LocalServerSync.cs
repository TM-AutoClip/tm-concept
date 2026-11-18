using Microsoft.Extensions.Logging;

namespace TMConcept.Core.Endpoints;

/// <summary>
/// The user's own Ollama and LM Studio in the model pickers (the developer, 18 Nov 2026: "Ollama and LM Studio models are not listed"):
/// before 0.1.24 a server had to be added by hand under Settings → Keys first. When the pickers load, the servers at their usual addresses
/// on this computer are looked for (at most once per <see cref="Interval"/>, a very short wait each); one that is not saved yet is saved
/// with every model it lists, so its installed models are offered in every text step like a hand-added server's (free, no key). A server
/// that is saved already is left as the user set it. Nothing is downloaded and nothing is sent but the servers' own model lists.
/// </summary>
public sealed class LocalServerSync(EndpointRegistry endpoints, IEndpointProbe probe, TimeProvider clock, ILogger<LocalServerSync> logger)
{
    /// <summary>The shortest time between two looks.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _last;

    /// <summary>
    /// Looks for the servers when the last look is older than <see cref="Interval"/> (or <paramref name="force"/>); returns how many servers
    /// were added. A failure is logged and adds nothing (the pickers still load).
    /// </summary>
    public async Task<int> SyncAsync(bool force, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = clock.GetUtcNow();
            if (!force && _last is { } last && now - last < Interval)
            {
                return 0;
            }

            _last = now;
            return await AddFoundAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The display name of a server kind (product names: not translated).</summary>
    public static string KindName(EndpointKind kind) => kind switch
    {
        EndpointKind.Ollama => "Ollama",
        EndpointKind.LmStudio => "LM Studio",
        _ => "Local server",
    };

    /// <summary>Whether a saved address and a found one are the same server (the same port on this computer, or the same host and port).</summary>
    public static bool SameServer(string saved, string found)
    {
        if (!Uri.TryCreate(saved, UriKind.Absolute, out var a) || !Uri.TryCreate(found, UriKind.Absolute, out var b) || a.Port != b.Port)
        {
            return false;
        }

        return (EndpointModelSource.OnThisComputer(saved) && EndpointModelSource.OnThisComputer(found))
            || string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A model the server listed, as a saved model of the server (the editor's defaults: its context, an answer limit from it).</summary>
    public static EndpointModel Model(ListedModel listed)
    {
        ArgumentNullException.ThrowIfNull(listed);
        var context = listed.ContextTokens > 0 ? listed.ContextTokens : EndpointDefaults.ContextTokens;
        return new EndpointModel
        {
            Id = listed.Id,
            Name = listed.Name,
            ContextTokens = context,
            MaxOutputTokens = EndpointCodes.DefaultMaxOutput(context),
            Vision = listed.Vision,
            JsonSchema = listed.JsonSchema,
            CanThink = listed.CanThink,
            SizeBytes = listed.SizeBytes,
        };
    }

    private async Task<int> AddFoundAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DetectedServer> found;
        try
        {
            found = await probe.DetectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogWarning(ex, "Looking for Ollama and LM Studio on this computer failed; the pickers list the saved servers only");
            return 0;
        }

        var added = 0;
        foreach (var server in found.Where(s => s.Kind is EndpointKind.Ollama or EndpointKind.LmStudio
            && !endpoints.All.Any(e => SameServer(e.BaseUrl, s.Address))))
        {
            var listing = await probe.ListModelsAsync(server.Address, string.Empty, cancellationToken).ConfigureAwait(false);
            if (!listing.Ok || listing.Models.Count == 0)
            {
                logger.LogInformation("{Kind} found on this computer, but it lists no model ({Code}); not added", KindName(server.Kind),
                    listing.Ok ? "none" : listing.Code);
                continue;
            }

            var endpoint = new LocalEndpoint
            {
                Name = KindName(server.Kind),
                BaseUrl = listing.Address.Length > 0 ? listing.Address : server.Address,
                Kind = listing.Kind == EndpointKind.Generic ? server.Kind : listing.Kind,
                Models = [.. listing.Models.Select(Model)],
                CreatedAtUtc = clock.GetUtcNow(),
            };
            await endpoints.SaveAsync(endpoint, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("{Kind} found on this computer and added with its {Count} model(s): they are offered in the text steps",
                endpoint.Name, endpoint.Models.Count);
            added++;
        }

        return added;
    }
}
