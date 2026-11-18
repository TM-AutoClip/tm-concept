using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;

namespace TMConcept.Core.Documents;

/// <summary>
/// Reads a scanned page with a model that reads images (0.1.24, the "text extraction" step): online (paid per page) or a local vision model
/// (free, on this computer). One call per page with the page's images; the answer is the page's text as printed, in its own language and
/// script. What every call used is charged to <see cref="Run"/> as it comes (a truncated answer keeps its part and is still charged), so the
/// month's spending counts the pages read before a cancel or a stop.
/// </summary>
public sealed class VisionPageRecognizer(IChatClient client, ResolvedModel model, ICostCalculator costs, Run run, ILogger logger) : PageTextRecognizerBase(logger)
{
    /// <summary>The instruction of every page call (the page's language is kept, nothing is translated).</summary>
    public const string Instruction =
        "You transcribe scanned document pages. Write out all the text on the page exactly as printed, in reading order (columns from left to right), "
        + "in its own language and script, with every accent and special letter (ç, ğ, ı, İ, ö, ş, ü…). Keep paragraphs apart with a blank line. "
        + "Leave out page numbers and running headers. Write nothing else: no comments, no translation, no Markdown. If the page has no text, answer with nothing.";

    /// <summary>Longest answer of one page (a dense encyclopedia page is about 1,500–2,500 tokens).</summary>
    public const int PageOutputMax = 4_000;

    /// <inheritdoc />
    public override string Source => PageTextSources.VisionPrefix + model.Model.Provider + "/" + model.Model.Id;

    /// <summary>The run the calls are charged to.</summary>
    public Run Run { get; } = run;

    /// <inheritdoc />
    protected override async Task<string> ReadAsync(PageImage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        try
        {
            var answer = await client.SendAsync(new ChatRequest(Instruction, "Page " + page.Page + ".", MaxOutputTokens: Math.Min(PageOutputMax,
                model.Model.MaxOutputTokens > 0 ? model.Model.MaxOutputTokens : PageOutputMax), Images: page.Images), cancellationToken).ConfigureAwait(false);
            Charge(answer.Usage, answer.ReportedCostUsd);
            return answer.Text;
        }
        catch (ProviderException ex) when (ex.ChargedUsage is { } charged)
        {
            Charge(charged, null);
            if (ex.Code is ("truncated" or "incomplete") && ex.PartialText.Length > 0)
            {
                return ex.PartialText;
            }

            throw;
        }
    }

    private void Charge(ChatUsage usage, decimal? reported)
    {
        // One page at a time: the run is never charged from two calls at once.
        Run.InputTokens += usage.Input;
        Run.CachedTokens += usage.CacheRead;
        Run.CacheWriteTokens += usage.CacheWrite;
        Run.OutputTokens += usage.Output;
        Run.ActualUsd += costs.Exact(model.Model, usage, reported);
    }
}
