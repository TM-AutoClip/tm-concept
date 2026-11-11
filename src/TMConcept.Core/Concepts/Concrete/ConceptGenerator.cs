using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Concept generation. Estimate (no network): prompt tokens from the text; output = count × the profile's measured average ± 15 % (the
/// measured tokens already include thinking), or before the first run count × the size expected from the target length × the thinking
/// share of the chosen effort (<see cref="ConceptOutputBudget.ThinkingShare"/>); a batch never costs more output than its max_tokens.
/// Generate: batches as large as fit the batch's max_tokens at the high end of that output (<see cref="BatchSizeAsync"/>, 20 at most; 0.1.7
/// sent 20 whatever the length and effort, so a long Turkish profile at effort high was cut off after about eight); before every batch the profile's past is read again (<see cref="IPromptMemorySource"/>; so a batch also avoids the
/// concepts of the previous batches; it is in the user part, so the cached system part stays the same for the whole run), the answer is parsed, the local
/// duplicate check throws away duplicates and marks similar ones, the batch is saved at once and the run row is updated with the exact
/// cost. Every batch asks for <see cref="ConceptOutputBudget.MaxOutputTokens"/> (the model's limit, at most 64 000) and logs it. A batch
/// cut off at max_tokens keeps every complete concept before the cut (they were paid for) and then ends the run with "truncated"; no
/// extra batch is started on its own (the page asks the user, with the estimate, before paying again). Cancel or an error keeps the finished batches. No automatic extra batch is made for thrown-away duplicates: the result
/// tells how many are missing. What is written comes from the <see cref="ConceptBrief"/> alone (0.1.13): a <see cref="TopicBrief"/> gives
/// one concept per ticked topic, in order, each batch its own slice and no extra request; an <see cref="IdeaBrief"/> gives its count and its
/// idea as the extra request, never topics. Every batch reports the topics that got a concept (<see cref="ConceptBatch.TopicsMade"/>).
/// </summary>
public sealed class ConceptGenerator(IConceptStore concepts, IProfileStore profiles, IRunStore runs, IChatClientFactory chats, ICostCalculator costs,
    IPromptMemorySource memory, IEmbeddingStore embeddings, IEmbeddingService embedder, TimeProvider clock, ILogger<ConceptGenerator> logger)
{
    private readonly List<ConceptEmbedding> _pendingEmbeddings = [];

    /// <summary>Band around a measured per-concept output (it already contains the thinking tokens).</summary>
    public static readonly (double Low, double High) MeasuredBand = (0.85, 1.15);

    /// <summary>
    /// The estimate of a run of <paramref name="brief"/> (<paramref name="focus"/>: the chosen focus line, empty = none;
    /// <paramref name="effort"/>: the chosen effort, empty = automatic): every batch's prompt as <see cref="GenerateAsync"/> sends it.
    /// </summary>
    public async Task<CostEstimate> EstimateAsync(Profile profile, ConceptBrief brief, string focus, Models.CatalogModel model, string effort,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(model);
        var count = brief.Count;
        var images = await profiles.ImagesAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var past = await memory.ReadAsync(profile, cancellationToken).ConfigureAwait(false);
        var (perConcept, band) = await OutputPerConceptAsync(profile, model, effort, cancellationToken).ConfigureAwait(false);
        var roof = ConceptOutputBudget.MaxOutputTokens(model);
        var total = CostEstimate.Zero;
        var first = true;
        var offset = 0;
        foreach (var size in Batches(count, ConceptOutputBudget.BatchSize(roof, perConcept * band.High)))
        {
            var prompt = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, images, past, brief.ExtraPrompt, size, focus, brief.Topics(offset, size)));
            offset += size;
            var system = costs.Tokens(prompt.System, profile.ContentLanguage);
            var user = costs.Tokens(prompt.User, profile.ContentLanguage);
            var output = size * perConcept;
            total = total.Plus(costs.EstimateChat(model, system + user, first ? 0 : system, (long)Math.Min(output * band.Low, roof),
                (long)Math.Min(output * band.High, roof)));
            first = false;
        }

        return total;
    }

    /// <summary>
    /// The estimate of the semantic duplicate check: title and summary of every new concept are embedded once (about
    /// <see cref="EmbeddingTokensPerConcept"/> tokens each), and so are the profile's earlier concepts that have no vector of this model yet
    /// (the backfill: up to <see cref="BackfillLimit"/> per batch of 20; 0.1.6 left them out of the estimate). A local model costs nothing,
    /// but the backfill count is still returned: embedding hundreds of earlier concepts takes a while on this computer.
    /// </summary>
    public async Task<(CostEstimate Estimate, int Backfill)> EstimateEmbeddingAsync(Profile profile, Models.CatalogModel model, int count,
        CancellationToken cancellationToken, int batchSize = ConceptLimits.BatchSize)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(model);
        count = Math.Clamp(count, ConceptLimits.CountMin, ConceptLimits.CountMax);
        var history = await concepts.HistoryAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var embedded = (await embeddings.ListAsync(profile.Id, model.Id, cancellationToken).ConfigureAwait(false)).Select(e => e.ConceptId).ToHashSet();
        var missing = history.Count(h => !embedded.Contains(h.Id));
        var backfill = Math.Min(missing, BackfillLimit * Batches(count, batchSize).Count());
        if (model.Provider == Models.ModelProviders.Local)
        {
            return (CostEstimate.Zero, backfill);
        }

        var tokens = (count + backfill) * (long)Math.Ceiling(EmbeddingTokensPerConcept * Profiles.OutputLanguages.Of(profile.ContentLanguage).EmbeddingTokenFactor);
        var usd = tokens * model.InputUsd / 1_000_000m;
        return (new CostEstimate(usd, usd, tokens, 0, 0), backfill);
    }

    /// <summary>
    /// Concepts per batch for <paramref name="model"/> at <paramref name="effort"/>: as many as fit the batch's max_tokens at the high end of
    /// the expected output per concept (thinking included), so a full batch is not cut off by its own roof (20 at most).
    /// </summary>
    public async Task<int> BatchSizeAsync(Profile profile, Models.CatalogModel model, string effort, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(model);
        var (perConcept, band) = await OutputPerConceptAsync(profile, model, effort, cancellationToken).ConfigureAwait(false);
        return ConceptOutputBudget.BatchSize(ConceptOutputBudget.MaxOutputTokens(model), perConcept * band.High);
    }

    /// <summary>Tokens of one concept's title and summary in English (other languages: <see cref="Profiles.OutputLanguage.EmbeddingTokenFactor"/>; Turkish about twice).</summary>
    public const int EmbeddingTokensPerConcept = 80;

    /// <summary>
    /// Generates in batches; <paramref name="progress"/> gets every saved batch, <paramref name="live"/> the steps of each batch's model call
    /// (sent, thinking, writing with the complete concepts so far, done).
    /// </summary>
    public async Task<ConceptRunResult> GenerateAsync(ConceptRequest request, IProgress<ConceptBatch>? progress, IProgress<ConceptCallProgress>? live,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = request.Profile;
        var brief = request.Brief;
        var count = brief.Count;
        var estimate = await EstimateAsync(profile, brief, request.Focus, request.Model.Model, request.Model.Effort, cancellationToken).ConfigureAwait(false);
        var run = new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.Concepts,
            Provider = request.Model.Model.Provider,
            Model = request.Model.Model.Id,
            KeyLabel = request.Model.Key.Label,
            Requested = count,
            ExtraPrompt = brief.ExtraPrompt,
            EstimateMinUsd = estimate.MinUsd,
            EstimateMaxUsd = estimate.MaxUsd,
            CostExact = true,
            StartedAtUtc = clock.GetUtcNow(),
        };
        await runs.AddAsync(run, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Concept run {Run}: {Count} concepts with {Provider}/{Model}{Topics}", run.Id, count, run.Provider, run.Model,
            brief is TopicBrief ? ", one per ticked topic" : string.Empty);

        var images = await profiles.ImagesAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var client = chats.Create(request.Model.Model, request.Model.Key.Secret);
        var accepted = new List<Concept>();
        var rejected = 0;
        var problems = 0;
        var sizes = Batches(count, await BatchSizeAsync(profile, request.Model.Model, request.Model.Effort, cancellationToken).ConfigureAwait(false)).ToList();
        var errorCode = string.Empty;
        var errorDetail = string.Empty;
        var semanticSkipped = 0;

        // The offset of the next batch's topic slice. Concepts come back in topic order, so of a cut or short answer the first ones of the
        // slice got a concept; a concept thrown away as a duplicate still counts: its topic is among the concepts.
        var topicsDone = 0;
        try
        {
            for (var index = 0; index < sizes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var subjects = await concepts.HistoryAsync(profile.Id, cancellationToken).ConfigureAwait(false);
                var past = await memory.ReadAsync(profile, cancellationToken).ConfigureAwait(false);
                var slice = brief.Topics(topicsDone, sizes[index]);
                var prompt = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, images, past, brief.ExtraPrompt, sizes[index], request.Focus, slice));
                var maxTokens = ConceptOutputBudget.MaxOutputTokens(request.Model.Model);
                logger.LogInformation("Concept run {Run}: batch {Batch}/{Batches} of {Size} concepts with {Topics} topics, max_tokens {MaxTokens}, effort {Effort}, {Seconds} s {Language}",
                    run.Id, index + 1, sizes.Count, sizes[index], slice?.Count ?? 0, maxTokens, request.Model.Effort.Length > 0 ? request.Model.Effort : "automatic",
                    profile.TargetSeconds, profile.ContentLanguage);
                string answer;
                ProviderException? cut = null;
                var batchNumber = index + 1;
                var batchSize = sizes[index];
                IProgress<ChatProgress>? call = live is null
                    ? null
                    : new ForwardProgress<ChatProgress>(p => live.Report(new ConceptCallProgress(batchNumber, sizes.Count, batchSize, p,
                        Math.Min(batchSize, ConceptResponseParser.CompleteCount(p.Text)))));
                try
                {
                    var response = await client.SendAsync(new ChatRequest(prompt.System, prompt.User, CacheSystem: true, Schema: prompt.Schema,
                        MaxOutputTokens: maxTokens,
                        Effort: request.Model.Effort, Stream: true, Progress: call), cancellationToken).ConfigureAwait(false);
                    Charge(run, request, response.Usage, response.ReportedCostUsd);
                    answer = response.Text;
                }
                catch (ProviderException ex) when (ex.ChargedUsage is { } charged)
                {
                    Charge(run, request, charged, null);
                    if (ex.Code is not ("truncated" or "incomplete") || ex.PartialText.Length == 0)
                    {
                        throw;
                    }

                    // The cut-off answer was paid for: keep the complete concepts before the cut, then stop the run.
                    logger.LogWarning("Concept run {Run}: batch {Batch} was cut off ({Code}, stop reason {StopReason}, max_tokens {MaxTokens}, {Output} output tokens); keeping the complete concepts before the cut",
                        run.Id, index + 1, ex.Code, ex.StopReason.Length > 0 ? ex.StopReason : "-", maxTokens, charged.Output);
                    cut = ex;
                    answer = ex.PartialText;
                }
                catch (OperationCanceledException)
                {
                    // Canceled while the call ran: the provider may bill the prompt (and what was written so far, which is not known here).
                    // At least the prompt is counted, as an estimate, so the month's spending does not miss the call.
                    var input = costs.Tokens(prompt.System, profile.ContentLanguage) + costs.Tokens(prompt.User, profile.ContentLanguage);
                    run.InputTokens += input;
                    run.ActualUsd += costs.Exact(request.Model.Model, new ChatUsage(input, 0, 0, 0));
                    run.CostExact = false;
                    throw;
                }

                var parsed = ConceptResponseParser.Parse(answer);
                problems += parsed.Problems.Count;
                foreach (var problem in parsed.Problems)
                {
                    logger.LogWarning("Concept run {Run}: {Problem}", run.Id, problem);
                }

                if (parsed.Concepts.Count == 0)
                {
                    throw cut ?? new ProviderException("The answer had no readable concept.") { Code = "parse" };
                }

                var batchCandidates = parsed.Concepts.Take(sizes[index])
                    .Select(c => (Parsed: c, Subject: new DuplicateSubject(Guid.NewGuid(), 0, c.Title, c.Summary))).ToList();
                var verdicts = request.LocalCheck
                    ? DuplicateCheck.Sift(batchCandidates.Select(c => c.Subject), subjects, profile.ContentLanguage)
                    : batchCandidates.Select(c => new DuplicateVerdict(c.Subject, DuplicateResult.None)).ToList();
                var semantic = await SemanticAsync(request, run, verdicts, subjects, cancellationToken).ConfigureAwait(false);
                verdicts = semantic.Verdicts;
                semanticSkipped += semantic.Skipped ? 1 : 0;
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
                var saved = _pendingEmbeddings.Where(e => batch.Any(b => b.Id == e.ConceptId)).ToList();
                _pendingEmbeddings.Clear();
                if (saved.Count > 0)
                {
                    await embeddings.SaveAsync(saved, CancellationToken.None).ConfigureAwait(false);
                }

                accepted.AddRange(batch);
                run.Accepted = accepted.Count;
                run.RejectedDuplicates = rejected;
                await runs.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                topicsDone += sizes[index];
                progress?.Report(new ConceptBatch(index + 1, sizes.Count, batch, rejected, run.ActualUsd,
                    slice?.Take(batchCandidates.Count).ToList()));
                if (cut is not null)
                {
                    throw cut;
                }
            }

            run.Status = RunStatus.Succeeded;
            logger.LogInformation("Concept run {Run} finished: {Accepted} kept, {Rejected} duplicates thrown away, {Problems} unreadable, {Usd} USD, {Seconds:0} s",
                run.Id, accepted.Count, rejected, problems, run.ActualUsd, (clock.GetUtcNow() - run.StartedAtUtc).TotalSeconds);
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

        return new ConceptRunResult(run, accepted, rejected, problems, errorCode, errorDetail, semanticSkipped);
    }

    /// <summary>Batch sizes of a count at the largest batch (20, 20, …, rest).</summary>
    public static IEnumerable<int> Batches(int count) => Batches(count, ConceptLimits.BatchSize);

    /// <summary>Batch sizes of a count with <paramref name="size"/> per batch (size, size, …, rest; the size is kept within 1–20).</summary>
    public static IEnumerable<int> Batches(int count, int size)
    {
        size = Math.Clamp(size, 1, ConceptLimits.BatchSize);
        for (var left = count; left > 0; left -= size)
        {
            yield return Math.Min(left, size);
        }
    }

    /// <summary>
    /// The expected output of one concept and its band: the profile's measured average ± 15 % (it already holds the thinking), or before the
    /// first run the size expected from the target length × the thinking share of <paramref name="effort"/>.
    /// </summary>
    private async Task<(double PerConcept, (double Low, double High) Band)> OutputPerConceptAsync(Profile profile, Models.CatalogModel model, string effort,
        CancellationToken cancellationToken)
    {
        var average = await runs.AverageConceptOutputTokensAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return average > 0
            ? (average, MeasuredBand)
            : (ConceptOutputBudget.TokensPerConcept(profile.ContentLanguage, profile.TargetSeconds), ConceptOutputBudget.ThinkingShare(model, effort ?? string.Empty));
    }

    /// <summary>
    /// Layer 3 (optional): embeds the candidates that passed the local check, compares them with the stored vectors of the profile and
    /// with each other, drops duplicates, marks similar ones and stores the new vectors. A failure only skips this layer for the batch: it is
    /// logged with its cause and reported back (<c>Skipped</c>) so the page can tell the user.
    /// </summary>
    private async Task<(IReadOnlyList<DuplicateVerdict> Verdicts, bool Skipped)> SemanticAsync(ConceptRequest request, Run run, IReadOnlyList<DuplicateVerdict> verdicts,
        IReadOnlyList<DuplicateSubject> history, CancellationToken cancellationToken)
    {
        if (request.Embedding is not { } model || verdicts.All(v => !v.Result.Accepted))
        {
            return (verdicts, false);
        }

        try
        {
            var stored = await embeddings.ListAsync(request.Profile.Id, model.Model.Id, cancellationToken).ConfigureAwait(false);
            var byId = history.ToDictionary(h => h.Id);
            var pool = stored.Where(e => byId.ContainsKey(e.ConceptId)).Select(e => (Subject: byId[e.ConceptId], Vector: SemanticCheck.FromBytes(e.Vector))).ToList();
            await BackfillAsync(request, run, model, history, pool, cancellationToken).ConfigureAwait(false);
            var candidates = verdicts.Where(v => v.Result.Accepted).ToList();
            var result = await embedder.EmbedAsync(model, candidates.Select(v => SemanticCheck.Text(v.Subject.Title, v.Subject.Summary)).ToList(), cancellationToken)
                .ConfigureAwait(false);
            run.ActualUsd += result.Tokens * model.Model.InputUsd / 1_000_000m;
            var output = new List<DuplicateVerdict>();
            var keep = new List<ConceptEmbedding>();
            var index = 0;
            foreach (var verdict in verdicts)
            {
                if (!verdict.Result.Accepted)
                {
                    output.Add(verdict);
                    continue;
                }

                var vector = result.Vectors[index++];
                var semantic = SemanticCheck.Check(vector, pool);
                var chosen = semantic.Kind == DuplicateKind.Duplicate || (semantic.Kind == DuplicateKind.Similar && verdict.Result.Kind == DuplicateKind.New)
                    ? semantic
                    : verdict.Result;
                output.Add(verdict with { Result = chosen });
                if (chosen.Accepted)
                {
                    pool.Add((verdict.Subject, vector));
                    keep.Add(new ConceptEmbedding
                    {
                        ConceptId = verdict.Subject.Id,
                        ProfileId = request.Profile.Id,
                        Model = model.Model.Id,
                        Dimensions = vector.Length,
                        Vector = SemanticCheck.ToBytes(vector),
                    });
                }
            }

            _pendingEmbeddings.AddRange(keep);
            return (output, false);
        }
        catch (Exception ex) when (ex is ProviderException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Semantic duplicate check skipped for this batch ({Provider}/{Model})", model.Model.Provider, model.Model.Id);
            return (verdicts, true);
        }
    }

    /// <summary>
    /// The earlier concepts of the profile that have no vector of this model yet (made before the meaning check was turned on, or with
    /// another model) are embedded now, the newest <see cref="BackfillLimit"/> per batch (each batch reads the stored vectors again, so the
    /// next batch goes on with the older ones), and stored; so the check compares with them too.
    /// </summary>
    private async Task BackfillAsync(ConceptRequest request, Run run, Models.ResolvedModel model, IReadOnlyList<DuplicateSubject> history,
        List<(DuplicateSubject Subject, float[] Vector)> pool, CancellationToken cancellationToken)
    {
        var known = pool.Select(p => p.Subject.Id).ToHashSet();
        var missing = history.Where(h => !known.Contains(h.Id)).OrderByDescending(h => h.Number).Take(BackfillLimit).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var result = await embedder.EmbedAsync(model, missing.Select(h => SemanticCheck.Text(h.Title, h.Summary)).ToList(), cancellationToken).ConfigureAwait(false);
        run.ActualUsd += result.Tokens * model.Model.InputUsd / 1_000_000m;
        var rows = new List<ConceptEmbedding>();
        for (var i = 0; i < missing.Count; i++)
        {
            pool.Add((missing[i], result.Vectors[i]));
            rows.Add(new ConceptEmbedding
            {
                ConceptId = missing[i].Id,
                ProfileId = request.Profile.Id,
                Model = model.Model.Id,
                Dimensions = result.Vectors[i].Length,
                Vector = SemanticCheck.ToBytes(result.Vectors[i]),
            });
        }

        await embeddings.SaveAsync(rows, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Meaning check: {Count} earlier concepts without a vector of {Model} were embedded ({Tokens} tokens)", rows.Count,
            model.Model.Id, result.Tokens);
    }

    /// <summary>Earlier concepts embedded per batch at most when the meaning check is turned on or its model changes.</summary>
    public const int BackfillLimit = 300;

    private void Charge(Run run, ConceptRequest request, ChatUsage usage, decimal? reported)
    {
        run.InputTokens += usage.Input;
        run.CachedTokens += usage.CacheRead;
        run.CacheWriteTokens += usage.CacheWrite;
        run.OutputTokens += usage.Output;
        run.ActualUsd += costs.Exact(request.Model.Model, usage, reported);
    }
}
