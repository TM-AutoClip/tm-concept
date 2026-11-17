using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Concepts;

/// <summary>
/// <see cref="IPromptMemorySource"/>: every concept of the profile (title and summary only; the content only of the newest telling dislikes,
/// whose opening sentence is sent), its kept wishes and its video ratings, through <see cref="PromptMemoryBuilder"/>.
/// </summary>
public sealed class PromptMemorySource(IConceptStore concepts, IConceptWishStore wishes, IVideoRatingStore ratings) : IPromptMemorySource
{
    private static readonly ConceptStatus[] AllStatuses = Enum.GetValues<ConceptStatus>();

    /// <inheritdoc />
    public async Task<PromptMemory> ReadAsync(Profile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var all = await concepts.ListAsync(profile.Id, AllStatuses, false, cancellationToken).ConfigureAwait(false);
        var telling = all.Where(c => c.Status == ConceptStatus.Trashed && c.TrashReason == TrashReason.Telling)
            .OrderByDescending(c => c.DecidedAtUtc ?? c.CreatedAtUtc).ThenByDescending(c => c.Number).Take(PromptMemoryLimits.DislikesEach).ToList();
        foreach (var concept in telling)
        {
            if (await concepts.GetAsync(concept.Id, cancellationToken).ConfigureAwait(false) is { } full)
            {
                concept.Content = full.Content;
            }
        }

        var kept = await wishes.ListAsync(profile.Id, PromptMemoryLimits.WishesKept, cancellationToken).ConfigureAwait(false);
        var rated = await ratings.ListAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return PromptMemoryBuilder.Build(all, kept.Select(w => w.Text).ToList(), profile.ContentLanguage, profile.ConceptText, rated);
    }
}
