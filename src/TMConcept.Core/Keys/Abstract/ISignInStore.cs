namespace TMConcept.Core.Keys;

/// <summary>Sign-ins at providers (<see cref="SignInRecord"/>), one per provider, in the encrypted database.</summary>
public interface ISignInStore
{
    /// <summary>The sign-in of <paramref name="provider"/>; null when not signed in.</summary>
    Task<SignInRecord?> GetAsync(string provider, CancellationToken cancellationToken);

    /// <summary>Adds or replaces the sign-in of its provider.</summary>
    Task SaveAsync(SignInRecord record, CancellationToken cancellationToken);

    /// <summary>Removes the sign-in of <paramref name="provider"/>.</summary>
    Task DeleteAsync(string provider, CancellationToken cancellationToken);
}
