namespace TMConcept.Core.Models;

/// <summary>
/// A model that runs on another computer: an online provider's (paid, with an API key) or a user-added server that is not on this computer.
/// It never takes the graphics card (<see cref="IGpuCoordinator"/> gives it a lease at once).
/// </summary>
public interface IOnlineModel : IModel
{
    /// <summary>Whether a call needs one of the user's API keys (false for a user-added server).</summary>
    bool NeedsKey { get; }
}