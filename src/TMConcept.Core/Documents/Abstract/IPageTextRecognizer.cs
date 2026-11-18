namespace TMConcept.Core.Documents;

/// <summary>
/// Reads the text of a scanned page from its images (0.1.24): an online or local vision model (<see cref="VisionPageRecognizer"/>), or a
/// local OCR engine. A page that fails is logged and counted (<see cref="PageRecognition.Failed"/>); after
/// <see cref="DocumentLimits.RecognitionFailuresInARowMax"/> failures in a row it throws <see cref="PageRecognitionStoppedException"/>
/// (a wrong key or an empty balance fails every page, so the reader stops asking instead of paying for nothing and keeps what it read).
/// </summary>
public interface IPageTextRecognizer
{
    /// <summary>Pages read so far.</summary>
    int Recognized { get; }

    /// <summary>Pages that failed so far.</summary>
    int Failed { get; }

    /// <summary>Reads the text of <paramref name="page"/>.</summary>
    Task<PageRecognition> RecognizeAsync(PageImage page, CancellationToken cancellationToken);
}
