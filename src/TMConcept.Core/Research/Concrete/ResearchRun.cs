using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Core.Research;

/// <summary>
/// One research run of "Araştırmadan" (0.1.22), in two steps the user sees. <see cref="SearchAsync"/>: the fixed plan
/// (<see cref="ResearchPlanner"/>), every backend asked with at most <see cref="ResearchLimits.Concurrency"/> requests at a time within the
/// time limit, the hits grouped by entity (one Wikidata item in many languages is one group) and the pages to fetch picked
/// (<see cref="Pick"/>): nothing is fetched yet, the section shows the list. <see cref="SaveAsync"/>: the ticked pages fetched one by one
/// within the byte budget and the time limit, each saved into the profile's documents (text, passages, vectors; a page the profile already
/// has is reused). A backend or a page that fails is logged and reported with its reason, and the others go on; a cancel stops at once
/// (what was saved stays, and the caller learns which documents it added even then, so it can take them away again). Keys and fetched text
/// never go into the log: only backend ids, hosts and counts.
/// </summary>
public sealed class ResearchRun(DocumentLibrary library, ILogger<ResearchRun> logger)
{
    /// <summary>The id of the addresses backend (the user's own addresses are always picked first).</summary>
    public const string UrlBackendId = "url";

    /// <summary>Searches every backend of <paramref name="backends"/> for <paramref name="request"/> and picks the pages to fetch.</summary>
    public async Task<ResearchCandidates> SearchAsync(ResearchRequest request, IReadOnlyList<IResearchBackend> backends, IProgress<ResearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(backends);
        var plan = ResearchPlanner.Plan(request, backends);
        var byId = backends.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var answers = new IReadOnlyList<ResearchHit>[plan.Queries.Count];
        var failures = new ConcurrentQueue<(int Order, ResearchFailure Failure)>();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(request.TimeLimit);
        using var gate = new SemaphoreSlim(ResearchLimits.Concurrency);
        var done = 0;
        logger.LogInformation("Research: {Queries} searches over {Backends} backends in {Languages}", plan.Queries.Count, backends.Count, string.Join(",", plan.Languages));

        async Task AskAsync(int index)
        {
            var planned = plan.Queries[index];
            var backend = byId[planned.BackendId];
            answers[index] = [];
            try
            {
                await gate.WaitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Enqueue((index, new ResearchFailure(backend.Name, "time", planned.Query.Language)));
                return;
            }

            try
            {
                progress?.Report(new ResearchProgress(ResearchStage.Searching, Label(backend, planned.Query), Volatile.Read(ref done), plan.Queries.Count));
                answers[index] = await backend.SearchAsync(planned.Query, limit.Token).ConfigureAwait(false);
            }
            catch (DocumentRejectedException ex)
            {
                logger.LogWarning("Research: {Backend} search failed ({Code})", backend.Id, ex.Code);
                failures.Enqueue((index, new ResearchFailure(backend.Name, ex.Code, ex.Detail)));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Research: {Backend} did not answer within the time limit", backend.Id);
                failures.Enqueue((index, new ResearchFailure(backend.Name, "time", planned.Query.Language)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or System.Text.Json.JsonException)
            {
                logger.LogError(ex, "Research: {Backend} search failed", backend.Id);
                failures.Enqueue((index, new ResearchFailure(backend.Name, "network", ex.Message)));
            }
            finally
            {
                gate.Release();
                Interlocked.Increment(ref done);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, plan.Queries.Count).Select(AskAsync)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var cut = limit.IsCancellationRequested;

        // The first of every answer, then the second of every answer, …: no backend or language crowds out the others.
        var hits = answers.SelectMany((answer, query) => answer.Select(hit => (hit, query))).OrderBy(x => x.hit.Rank).ThenBy(x => x.query).Select(x => x.hit).ToList();
        var candidates = Pick(plan, hits, request.MaxPages);
        progress?.Report(new ResearchProgress(ResearchStage.Done, string.Empty, plan.Queries.Count, plan.Queries.Count));
        logger.LogInformation("Research: {Hits} hits, {Candidates} candidates, {Picked} picked, {Failures} failed searches{Cut}", hits.Count, candidates.Count,
            candidates.Count(c => c.Picked), failures.Count, cut ? ", stopped at the time limit" : string.Empty);
        return new ResearchCandidates(plan, candidates, failures.OrderBy(f => f.Order).Select(f => f.Failure).ToList(), cut);
    }

    /// <summary>
    /// The candidates of <paramref name="hits"/> (in the merged order): hits of one entity or one address are one group, and each group's
    /// version in the most preferred language of <paramref name="plan"/> is its main candidate (the profile's language, then English, …; an
    /// article's other languages come from its language links). The user's addresses are picked first, then the main candidates in order,
    /// while about a third of the pages left goes to the groups' versions in the other read languages (Greek for Byzantium), so the sources
    /// are not all in one language. With one or two pages left there is no such share: a small budget (a local 8k model) reads the
    /// profile's language and English first. Picked ones come first; at most <see cref="ResearchLimits.CandidatesMax"/> in all.
    /// </summary>
    public static IReadOnlyList<ResearchCandidate> Pick(ResearchPlan plan, IReadOnlyList<ResearchHit> hits, int maxPages)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(hits);
        maxPages = Math.Clamp(maxPages, 1, ResearchLimits.PagesMax);
        var order = new List<string>();
        var groups = new Dictionary<string, List<ResearchHit>>(StringComparer.Ordinal);
        foreach (var hit in hits)
        {
            var key = GroupKey(hit);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
                order.Add(key);
            }

            list.Add(hit);
        }

        var ranks = plan.Languages.Select((language, index) => (language, index)).ToDictionary(x => x.language, x => x.index, StringComparer.Ordinal);
        int Preference(string language) => ranks.TryGetValue(language, out var index) ? index : plan.Languages.Count;
        var users = new List<ResearchHit>();
        var main = new List<ResearchHit>();
        var others = new List<ResearchHit>();
        foreach (var key in order)
        {
            var found = groups[key];
            var versions = found.Concat(found.SelectMany(h => h.Alternates.Select(a => h with { Title = a.Title, Url = a.Url, Language = a.Language, Alternates = [] })))
                .GroupBy(h => h.Language, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(h => Preference(h.Language))
                .ToList();
            var first = versions[0] with { Alternates = [] };
            (first.BackendId == UrlBackendId ? users : main).Add(first);
            others.AddRange(versions.Skip(1).Where(v => Preference(v.Language) >= plan.SearchLanguages.Count && Preference(v.Language) < plan.Languages.Count));
        }

        var picked = new List<ResearchHit>(users.Take(maxPages));
        var otherQuota = Math.Min(others.Count, (maxPages - picked.Count) / 3);
        picked.AddRange(main.Take(Math.Max(0, maxPages - picked.Count - otherQuota)));
        picked.AddRange(others.Take(Math.Max(0, maxPages - picked.Count)));
        picked.AddRange(main.Except(picked).Take(Math.Max(0, maxPages - picked.Count)));
        var rest = users.Concat(main).Concat(others).Except(picked).Take(Math.Max(0, ResearchLimits.CandidatesMax - picked.Count));
        return picked.Select(h => new ResearchCandidate(h, true)).Concat(rest.Select(h => new ResearchCandidate(h, false))).ToList();
    }

    /// <summary>
    /// Fetches <paramref name="picked"/> one by one and saves each into the profile's documents, within the page count, the byte budget and
    /// the time limit of <paramref name="request"/>. A page that fails or does not fit is skipped with its reason (a page past the page count
    /// too); a page whose text could not be read is counted as added (so it is taken away with the others) but not used. Every document
    /// this run adds goes into <paramref name="added"/> the moment it is saved, so a cancel or an error midway still tells the caller what
    /// to take away.
    /// </summary>
    public async Task<ResearchSaved> SaveAsync(Guid profileId, IReadOnlyList<ResearchHit> picked, IReadOnlyList<IResearchBackend> backends, ResearchRequest request,
        ICollection<Guid>? added, IProgress<ResearchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(picked);
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(request);
        var byId = backends.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var documents = new List<SourceDocument>();
        var mine = new List<Guid>();
        var skipped = new List<ResearchFailure>();
        long bytes = 0;
        var cut = false;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(request.TimeLimit);
        var most = Math.Clamp(request.MaxPages, 1, ResearchLimits.PagesMax);
        var pages = picked.Take(most).ToList();
        if (picked.Count > most)
        {
            logger.LogWarning("Research: {Over} ticked pages are over the limit of {Most}", picked.Count - most, most);
            skipped.AddRange(picked.Skip(most).Select(p => new ResearchFailure(p.Title, "pages", most.ToString(CultureInfo.InvariantCulture))));
            cut = true;
        }

        var seconds = string.Create(CultureInfo.InvariantCulture, $"{request.TimeLimit.TotalSeconds:0} s");
        for (var i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hit = pages[i];
            if (!byId.TryGetValue(hit.BackendId, out var backend))
            {
                skipped.Add(new ResearchFailure(hit.Title, "backend", hit.BackendId));
                continue;
            }

            progress?.Report(new ResearchProgress(ResearchStage.Fetching, hit.Title, i, pages.Count));
            FetchedDocument fetched;
            try
            {
                fetched = await backend.FetchAsync(hit, limit.Token).ConfigureAwait(false);
            }
            catch (DocumentRejectedException ex)
            {
                logger.LogWarning("Research: a page of {Backend} was not fetched ({Code})", backend.Id, ex.Code);
                skipped.Add(new ResearchFailure(hit.Title, ex.Code, ex.Detail));
                continue;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Research: the time limit stopped the fetching after {Done} of {Total} pages", i, pages.Count);
                skipped.AddRange(pages.Skip(i).Select(p => new ResearchFailure(p.Title, "time", seconds)));
                cut = true;
                break;
            }

            if (bytes + fetched.Content.Length > request.MaxBytes)
            {
                logger.LogWarning("Research: a page of {Bytes} bytes did not fit the run's budget", fetched.Content.Length);
                skipped.Add(new ResearchFailure(hit.Title, "budget", string.Empty));
                cut = true;
                continue;
            }

            bytes += fetched.Content.Length;
            progress?.Report(new ResearchProgress(ResearchStage.Saving, hit.Title, i, pages.Count));
            DocumentAddResult result;
            try
            {
                result = await library.AddFetchedAsync(profileId, fetched, reuse: true, null, cancellationToken).ConfigureAwait(false);
            }
            catch (DocumentRejectedException ex)
            {
                logger.LogWarning("Research: a fetched page was not saved ({Code})", ex.Code);
                skipped.Add(new ResearchFailure(hit.Title, ex.Code, ex.Detail));
                continue;
            }

            if (!result.Existed)
            {
                mine.Add(result.Document.Id);
                added?.Add(result.Document.Id);
            }

            if (result.Document.Status == DocumentStatus.Ready && result.Document.Chunks > 0)
            {
                documents.Add(result.Document);
            }
            else
            {
                skipped.Add(new ResearchFailure(hit.Title, "no_text", string.Empty));
            }
        }

        progress?.Report(new ResearchProgress(ResearchStage.Done, string.Empty, pages.Count, pages.Count));
        logger.LogInformation("Research: {Saved} pages usable ({Added} new), {Skipped} skipped, {Bytes} bytes{Cut}", documents.Count, mine.Count, skipped.Count, bytes,
            cut ? ", a limit stopped it early" : string.Empty);
        return new ResearchSaved(documents, mine, skipped, bytes, cut);
    }

    /// <summary>The key of the group of <paramref name="hit"/>: its entity, else its normalized address, else its backend and title.</summary>
    public static string GroupKey(ResearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return hit.EntityKey.Length > 0 ? hit.EntityKey : ResearchText.UrlKey(hit.Url) is { Length: > 0 } key ? key : hit.BackendId + ":" + hit.Title;
    }

    private static string Label(IResearchBackend backend, ResearchQuery query) => backend.PerLanguage ? $"{backend.Name} ({query.Language})" : backend.Name;
}
