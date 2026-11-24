namespace TMConcept.Providers.GoogleAuth;

/// <summary>
/// "Sign in with Google" failed (0.1.47). <see cref="Code"/> says why and the interface turns it into text: <c>file.json</c> (the client
/// file is not JSON), <c>file.web</c> (a "Web application" client), <c>file.kind</c> (not an OAuth client file), <c>file.fields</c> (no
/// client id or secret), <c>project</c> (no project id), <c>state</c> (the answer to the local address did not carry this sign-in's
/// state), <c>denied</c> (refused in the browser), <c>code</c> (no code came back), <c>timeout</c> (no answer in time), <c>no_refresh</c>
/// (Google gave no refresh token), <c>expired</c> (the refresh token was refused: sign in again). Never holds a token or a secret.
/// </summary>
public sealed class GoogleSignInException : Exception
{
    /// <summary>Creates.</summary>
    public GoogleSignInException()
    {
    }

    /// <summary>With a message.</summary>
    public GoogleSignInException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and an inner error.</summary>
    public GoogleSignInException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Why.</summary>
    public string Code { get; init; } = "code";
}
