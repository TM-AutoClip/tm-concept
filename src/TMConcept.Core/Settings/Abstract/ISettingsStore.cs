namespace TMConcept.Core.Settings;

/// <summary>Reads and writes <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>Settings (defaults when none were saved).</summary>
    Task<AppSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>Saves.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
