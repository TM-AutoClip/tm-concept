using TMConcept.Core.Profiles;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Desktop;

/// <summary>The real profile store, whose next <see cref="SaveAsync"/> fails when <see cref="FailNextSave"/> is set (a busy database).</summary>
internal sealed class FlakyProfileStore(ProfileStore inner) : IProfileStore
{
    public bool FailNextSave { get; set; }

    public Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);

    public Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

    public Task SaveAsync(Profile profile, CancellationToken cancellationToken)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new IOException("database is locked");
        }

        return inner.SaveAsync(profile, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);
}
