using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.Concepts;

/// <summary>
/// Every status change of a concept the user makes by hand, in one place for every page (0.1.13; the concept page and the produced page
/// had their own copies): approve, take back from approved (Approved → New), move to the trash with a reason, and produce again
/// (Produced → Approved). Each one is saved, written to the activity log and told to the other pages (<see cref="ConceptEvents"/>, with the
/// page that asked as the sender, so that page does not reload its own change).
/// </summary>
public sealed class ConceptActions(IConceptStore concepts, ConceptEvents events, IActivityLog activity, Localizer text, ILogger<ConceptActions> logger)
{
    /// <summary>Approves a new concept.</summary>
    public Task ApproveAsync(Concept concept, object sender) => SetAsync(concept, ConceptStatus.Approved, "concepts.approved.done", sender);

    /// <summary>Takes an approved concept back to the new ones.</summary>
    public Task UnapproveAsync(Concept concept, object sender) => SetAsync(concept, ConceptStatus.New, "concepts.unapproved.done", sender);

    /// <summary>
    /// Moves concepts to the trash with <paramref name="reason"/> (a disliked topic or telling keeps the next prompts away from it; the
    /// duplicate check still counts a trashed concept).
    /// </summary>
    public async Task TrashAsync(IReadOnlyList<Concept> trashed, TrashReason reason, object sender)
    {
        ArgumentNullException.ThrowIfNull(trashed);
        if (trashed.Count == 0)
        {
            return;
        }

        await concepts.TrashAsync(trashed.Select(c => c.Id).ToList(), reason, CancellationToken.None).ConfigureAwait(true);
        foreach (var concept in trashed)
        {
            concept.Status = ConceptStatus.Trashed;
            activity.Add(ActivityLevel.Info, text.Format("concepts.trashed.done", Title(concept)),
                text.Format("concepts.trashed.done", ActivityPlaceholders.Title));
        }

        logger.LogInformation("{Count} concepts moved to the trash ({Reason})", trashed.Count, reason);
        events.Raise(sender);
    }

    /// <summary>
    /// Puts the concept of a made video back among the approved ones, so it can be fixed and made again. Not done (with the reason's key)
    /// when the concept is gone or already approved or queued.
    /// </summary>
    public async Task<(bool Ok, string ErrorKey)> ProduceAgainAsync(Guid? conceptId, object sender)
    {
        var concept = conceptId is { } id ? await concepts.GetAsync(id, CancellationToken.None).ConfigureAwait(true) : null;
        if (concept is null)
        {
            return (false, "produced.again.gone");
        }

        if (concept.Status is ConceptStatus.Queued or ConceptStatus.Approved)
        {
            return (false, "produced.again.already");
        }

        await SetAsync(concept, ConceptStatus.Approved, "produced.again.done", sender).ConfigureAwait(true);
        return (true, string.Empty);
    }

    private async Task SetAsync(Concept concept, ConceptStatus status, string doneKey, object sender)
    {
        ArgumentNullException.ThrowIfNull(concept);
        await concepts.SetStatusAsync([concept.Id], status, CancellationToken.None).ConfigureAwait(true);
        concept.Status = status;
        activity.Add(ActivityLevel.Info, text.Format(doneKey, Title(concept)), text.Format(doneKey, ActivityPlaceholders.Title));
        events.Raise(sender);
    }

    private static string Title(Concept concept) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"#{concept.Number} {concept.Title}");
}
