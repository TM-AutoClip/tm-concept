using Microsoft.Extensions.Logging;
using TMConcept.Core.Concepts;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.Reports;

/// <summary>
/// The names a problem report masks by value: every profile name, concept title and video title in the database, as written and as a
/// folder name (<see cref="LibraryPaths.SafeName"/>). The log lines of 0.1.4 already leave them out; this also covers older log files and
/// exception messages. With the database locked there are none (the report says so in system.txt).
/// </summary>
public sealed class ReportNames(Vault vault, IProfileStore profiles, IConceptStore concepts, IVideoStore videos, ILogger<ReportNames> logger)
{
    /// <summary>The names, or <c>null</c> when the database is not open or could not be read.</summary>
    public async Task<IReadOnlyCollection<string>?> ListAsync(CancellationToken cancellationToken)
    {
        if (vault.State != VaultState.Open)
        {
            logger.LogInformation("The app is locked: the problem report masks folders, e-mail addresses and keys, but not names by value");
            return null;
        }

        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var statuses = Enum.GetValues<ConceptStatus>();
            foreach (var profile in await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                Add(names, profile.Name);
                foreach (var concept in await concepts.ListAsync(profile.Id, statuses, withContent: false, cancellationToken).ConfigureAwait(false))
                {
                    Add(names, concept.Title);
                }

                foreach (var video in await videos.ListAsync(profile.Id, cancellationToken).ConfigureAwait(false))
                {
                    Add(names, video.Title);
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The names to mask could not be read from the database; the problem report masks folders, e-mail addresses and keys only");
            return null;
        }
    }

    private static void Add(HashSet<string> names, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        names.Add(name.Trim());
        names.Add(LibraryPaths.SafeName(name, string.Empty));
    }
}
