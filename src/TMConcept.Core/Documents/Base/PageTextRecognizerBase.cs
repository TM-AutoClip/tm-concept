using Microsoft.Extensions.Logging;
using TMConcept.Core.Providers;

namespace TMConcept.Core.Documents;

/// <summary>
/// The common part of reading a scanned page (0.1.24): the engine's own reading (<see cref="ReadAsync"/>), counted; a failure is logged as
/// an error with the page and counted, and the page is left out; a failure every further page would repeat (<c>auth</c>, <c>billing</c>,
/// <c>model</c>) or <see cref="DocumentLimits.RecognitionFailuresInARowMax"/> failures in a row stop the add
/// (<see cref="PageRecognitionStoppedException"/>). A cancel goes up as it is.
/// </summary>
public abstract class PageTextRecognizerBase(ILogger logger) : IPageTextRecognizer
{
    private static readonly string[] Fatal = ["auth", "billing", "model"];
    private int _inARow;

    /// <inheritdoc />
    public int Recognized { get; private set; }

    /// <inheritdoc />
    public int Failed { get; private set; }

    /// <inheritdoc />
    public async Task<PageRecognition> RecognizeAsync(PageImage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        string text;
        try
        {
            text = await ReadAsync(page, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not PageRecognitionStoppedException)
        {
            Failed++;
            _inARow++;
            logger.LogError(ex, "Text extraction of page {Page} failed ({Failed} failed so far)", page.Page, Failed);
            if ((ex is ProviderException provider && Fatal.Contains(provider.Code, StringComparer.Ordinal)) || _inARow >= DocumentLimits.RecognitionFailuresInARowMax)
            {
                throw new PageRecognitionStoppedException("Text extraction stopped after page " + page.Page + ": " + ex.Message, ex);
            }

            return PageRecognition.Failure;
        }

        _inARow = 0;
        Recognized++;
        return new PageRecognition(text.Trim(), false);
    }

    /// <summary>The text on <paramref name="page"/> (empty when it has none); throws when it cannot be read.</summary>
    protected abstract Task<string> ReadAsync(PageImage page, CancellationToken cancellationToken);
}
