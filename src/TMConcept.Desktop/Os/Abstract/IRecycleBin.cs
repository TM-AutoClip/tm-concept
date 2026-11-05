namespace TMConcept.Desktop.Os;

/// <summary>Moves a folder to the system's recycle bin / trash (never deletes it outright).</summary>
public interface IRecycleBin
{
    /// <summary>Moves the folder; throws when it cannot.</summary>
    Task MoveAsync(string folder, CancellationToken cancellationToken);
}
