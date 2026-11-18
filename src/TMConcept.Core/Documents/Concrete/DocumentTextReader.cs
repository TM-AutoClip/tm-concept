using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Runs;

namespace TMConcept.Core.Documents;

/// <summary>
/// The text extraction of scanned PDF pages while a document is added (0.1.24): the estimate before (pages × an image and the instruction in,
/// a page of text out; the price from the model's price table, a rough time), and the run the calls are charged to
/// (<see cref="RunKind.DocumentText"/>: the month's spending counts it, also after a cancel or a stop).
/// </summary>
public sealed class DocumentTextReader(IChatClientFactory chats, ICostCalculator costs, IRunStore runs, TimeProvider clock, ILogger<DocumentTextReader> logger)
{
    /// <summary>Prompt tokens a page image is counted as (providers bill a full page scan at about 1,000–1,800 tokens).</summary>
    public const int ImageTokensPerPage = 1_600;

    /// <summary>Prompt tokens of the instruction besides the image.</summary>
    public const int InstructionTokens = 150;

    /// <summary>Answer tokens of a light page (a title page, a short article end).</summary>
    public const int PageOutputMin = 300;

    /// <summary>Answer tokens of a dense page (an encyclopedia page in two columns).</summary>
    public const int PageOutputMax = 2_000;

    /// <summary>The estimate of reading <paramref name="pages"/> scanned pages with <paramref name="model"/> (graphics card of <paramref name="cardBytes"/>).</summary>
    public WorkEstimate Estimate(CatalogModel model, int pages, long cardBytes)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (pages <= 0)
        {
            return WorkEstimate.None;
        }

        var cost = costs.EstimateChat(model, (long)pages * (ImageTokensPerPage + InstructionTokens), 0, (long)pages * PageOutputMin, (long)pages * PageOutputMax);
        return ModelSpeed.Estimate(model, cardBytes, pages, cost);
    }

    /// <summary>Starts a run of reading about <paramref name="pages"/> pages of <paramref name="name"/> with <paramref name="model"/> and gives the recognizer that charges it.</summary>
    public async Task<VisionPageRecognizer> StartAsync(Guid profileId, ResolvedModel model, string name, int pages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var estimate = Estimate(model.Model, pages, 0).Cost;
        var run = new Run
        {
            ProfileId = profileId,
            Kind = RunKind.DocumentText,
            Provider = model.Model.Provider,
            Model = model.Model.Id,
            KeyLabel = model.Key.Label,
            Requested = pages,
            ExtraPrompt = name ?? string.Empty,
            EstimateMinUsd = estimate.MinUsd,
            EstimateMaxUsd = estimate.MaxUsd,
            CostExact = true,
            StartedAtUtc = clock.GetUtcNow(),
        };
        await runs.AddAsync(run, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Document text {Run}: up to {Pages} scanned pages with {Provider}/{Model}", run.Id, pages, model.Model.Provider, model.Model.Id);
        return new VisionPageRecognizer(chats.Create(model.Model, model.Key.Secret), model, costs, run, logger);
    }

    /// <summary>Ends the run of <paramref name="recognizer"/>: its pages, its outcome and its cost are saved.</summary>
    public async Task FinishAsync(VisionPageRecognizer recognizer, RunStatus status, string error)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        var run = recognizer.Run;
        run.Accepted = recognizer.Recognized;
        run.Status = status;
        run.Error = error ?? string.Empty;
        run.FinishedAtUtc = clock.GetUtcNow();
        await runs.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Document text {Run}: {Read} pages read, {Failed} failed, {Status}, {Usd} USD", run.Id, recognizer.Recognized, recognizer.Failed, status,
            run.ActualUsd);
    }
}
