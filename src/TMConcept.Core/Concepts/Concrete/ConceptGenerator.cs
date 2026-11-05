using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Concept generation. Estimate (no network): prompt tokens from the text, output = count × the profile's measured average (first guess
/// 700) × 1.1–1.5. Generate: batches of 20; before every batch the history is read again (so a batch also avoids the concepts of the
/// previous batches), the answer is parsed, the local duplicate check throws away duplicates and marks similar ones, the batch is saved at
/// once and the run row is updated with the exact cost. Cancel or an error keeps the finished batches. No automatic extra batch is made
/// for thrown-away duplicates: the result tells how many are missing.
/// </summary>
public sealed class ConceptGenerator(IConceptStore concepts, IProfileStore profiles, IRunStore runs, IChatClientFactory chats, ICostCalculator costs,
    TimeProvider clock, ILogger<ConceptGenerator> logger)
{
    /// <summary>Thinking share on top of the output (low and high end of the range).</summary>
    public static readonly (double Low, double High) ThinkingShare = (1.1, 1.5);

    /// <summary>The estimate of a press.</summary>
    public async Task<CostEstimate> EstimateAsync(Profile profile, int count, string extraPrompt, Models.CatalogModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(model);
        count = Math.Clamp(count, ConceptLimits.CountMin, ConceptLimits.CountMax);
        var images = await profiles.ImagesAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var history = await HistoryTitlesAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var average = await runs.AverageConceptOutputTokensAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var perConcept = average > 0 ? average : NarrationRate.FirstGuessOutputTokens;
        var total = CostEstimate.Zero;
        var first = true;
        foreach (var size in Batches(count))
        {
            var prompt = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, images, history, extraPrompt, size));
            var system = costs.Tokens(prompt.System, profile.ContentLanguage);
            var user = costs.Tokens(prompt.User, profile.ContentLanguage);
            var output = size * perConcept;
            total = total.Plus(costs.EstimateChat(model, system + user, first ? 0 : system, (long)(output * ThinkingShare.Low), (long)(output * ThinkingShare.High)));
            first = false;
        }

        return total;
    }

    /// <summary>Generates in batches; <paramref name="progress"/> gets every saved batch.</summary>
    public async Task<ConceptRunResult> GenerateAsync(ConceptRequest request, IProgress<ConceptBatch>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = request.Profile;
        var count = Math.Clamp(request.Count, ConceptLimits.CountMin, ConceptLimits.CountMax);
        var estimate = await EstimateAsync(profile, count, request.ExtraPrompt, request.Model.Model, cancellationToken).ConfigureAwait(false);
        var run = new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.Concepts,
            Provider = request.Model.Model.Provider,
            Model = request.Model.Model.Id,
            KeyLabel = request.Model.Key.Label,
            Requested = count,
            ExtraPrompt = request.ExtraPrompt,
            EstimateMinUsd = estimate.MinUsd,
            EstimateMaxUsd = estimate.MaxUsd,
            CostExact = true,
            StartedAtUtc = clock.GetUtcNow(),
        };
        await runs.AddAsync(run, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Concept run {Run}: {Count} concepts with {Provider}/{Model}", run.Id, count, run.Provider, run.Model);

        var images = await profiles.ImagesAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var client = chats.Create(request.Model.Model, request.Model.Key.Secret);
        var accepted = new List<Concept>();
        var rejected = 0;
        var problems = 0;
        var sizes = Batches(count).ToList();
        var errorCode = string.Empty;
        var errorDetail = string.Empty;
        try
        {
            for (var index = 0; index < sizes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var subjects = await concepts.HistoryAsync(profile.Id, cancellationToken).ConfigureAwait(false);
                var prompt = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, images, Titles(subjects), request.ExtraPrompt, sizes[index]));
                ChatResponse response;
                try
                {
                    response = await client.SendAsync(new ChatRequest(prompt.System, prompt.User, CacheSystem: true, Schema: prompt.Schema,
                        MaxOutputTokens: MaxOutput(sizes[index]), Effort: request.Model.Effort), cancellationToken).ConfigureAwait(false);
                }
                catch (ProviderException ex) when (ex.ChargedUsage is { } charged)
                {
                    Charge(run, request, charged, null);
                    throw;
                }

                Charge(run, request, response.Usage, response.ReportedCostUsd);
                var parsed = ConceptResponseParser.Parse(response.Text);
                problems += parsed.Problems.Count;
                foreach (var problem in parsed.Problems)
                {
                    logger.LogWarning("Concept run {Run}: {Problem}", run.Id, problem);
                }

                if (parsed.Concepts.Count == 0)
                {
                    throw new ProviderException("The answer had no readable concept.") { Code = "parse" };
                }

                var batchCandidates = parsed.Concepts.Take(sizes[index])
                    .Select(c => (Parsed: c, Subject: new DuplicateSubject(Guid.NewGuid(), 0, c.Title, c.Summary))).ToList();
                var verdicts = request.LocalCheck
                    ? DuplicateCheck.Sift(batchCandidates.Select(c => c.Subject), subjects)
                    : batchCandidates.Select(c => new DuplicateVerdict(c.Subject, DuplicateResult.None)).ToList();
                var batch = new List<Concept>();
                foreach (var (candidate, verdict) in batchCandidates.Zip(verdicts))
                {
                    if (!verdict.Result.Accepted)
                    {
                        rejected++;
                        logger.LogInformation("Concept run {Run}: a duplicate of #{Number} was thrown away", run.Id, verdict.Result.OtherNumber);
                        continue;
                    }

                    batch.Add(new Concept
                    {
                        Id = candidate.Subject.Id,
                        ProfileId = profile.Id,
                        Title = candidate.Parsed.Title,
                        Summary = candidate.Parsed.Summary,
                        Content = candidate.Parsed.Content,
                        Language = profile.ContentLanguage,
                        RunId = run.Id,
                        SimilarToId = verdict.Result.Kind == DuplicateKind.Similar ? verdict.Result.OtherId : null,
                        Similarity = verdict.Result.Kind == DuplicateKind.Similar ? verdict.Result.Similarity : null,
                        DuplicateNote = verdict.Result.Code,
                        CreatedAtUtc = clock.GetUtcNow(),
                    });
                }

                await concepts.AddAsync(batch, CancellationToken.None).ConfigureAwait(false);
                accepted.AddRange(batch);
                run.Accepted = accepted.Count;
                run.RejectedDuplicates = rejected;
                await runs.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                progress?.Report(new ConceptBatch(index + 1, sizes.Count, batch, rejected, run.ActualUsd));
            }

            run.Status = RunStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            run.Status = RunStatus.Canceled;
            errorCode = "canceled";
            logger.LogInformation("Concept run {Run} canceled after {Count} concepts", run.Id, accepted.Count);
        }
        catch (ProviderException ex)
        {
            run.Status = RunStatus.Failed;
            run.Error = ex.Code;
            errorCode = ex.Code;
            errorDetail = ex.Message;
            logger.LogError("Concept run {Run} failed ({Code}, request {RequestId}): {Message}", run.Id, ex.Code, ex.RequestId.Length > 0 ? ex.RequestId : "-",
                ex.Message);
        }
        finally
        {
            run.FinishedAtUtc = clock.GetUtcNow();
            run.Accepted = accepted.Count;
            run.RejectedDuplicates = rejected;
            await runs.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
        }

        return new ConceptRunResult(run, accepted, rejected, problems, errorCode, errorDetail);
    }

    /// <summary>Batch sizes of a count (20, 20, …, rest).</summary>
    public static IEnumerable<int> Batches(int count)
    {
        for (var left = count; left > 0; left -= ConceptLimits.BatchSize)
        {
            yield return Math.Min(left, ConceptLimits.BatchSize);
        }
    }

    private static int MaxOutput(int count) => Math.Min(64_000, 4_000 + (count * 1_600));

    private static List<string> Titles(IReadOnlyList<DuplicateSubject> subjects) => subjects.OrderBy(s => s.Number).Select(s => s.Title).ToList();

    private async Task<List<string>> HistoryTitlesAsync(Guid profileId, CancellationToken cancellationToken) =>
        Titles(await concepts.HistoryAsync(profileId, cancellationToken).ConfigureAwait(false));

    private void Charge(Run run, ConceptRequest request, ChatUsage usage, decimal? reported)
    {
        run.InputTokens += usage.Input;
        run.CachedTokens += usage.CacheRead;
        run.CacheWriteTokens += usage.CacheWrite;
        run.OutputTokens += usage.Output;
        run.ActualUsd += costs.Exact(request.Model.Model, usage, reported);
    }
}
