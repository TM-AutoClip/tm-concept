using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Providers;

namespace TMConcept.Providers.GoogleAuth;

/// <summary>
/// "Sign in with Google" for Google Cloud Text-to-Speech (0.1.47, H3 decision A: the user's own desktop OAuth client; the API key stays as
/// C). <see cref="SignInAsync"/> runs the loopback flow with PKCE S256 and a state: a one-time listener on 127.0.0.1, the browser opened
/// (when it cannot be, the page shows the address to copy), the code turned into tokens, the listener closed; nothing waits on the
/// window's thread and the page's Cancel ends it. The refresh token and the client are kept in the encrypted database
/// (<see cref="ISignInStore"/>); the access token only in memory, renewed by itself shortly before it ends (<see cref="AccessAsync"/>).
/// <see cref="SignOutAsync"/> deletes the sign-in here first, then asks Google to revoke it. Tokens and the client secret are never logged.
/// </summary>
public sealed class GoogleCloudSignIn(ISignInStore store, GoogleTokenClient tokens, TimeProvider clock, ILogger<GoogleCloudSignIn> logger) : IProviderSignIn
{
    /// <summary>The provider code (the Google Cloud Text-to-Speech provider's).</summary>
    public const string ProviderCode = "google-cloud";

    /// <summary>An access token is renewed when it ends within this time.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(2);

    /// <summary>The longest wait for the browser to come back.</summary>
    public static readonly TimeSpan BrowserWait = TimeSpan.FromMinutes(10);

    // Never disposed: without AvailableWaitHandle it holds nothing unmanaged (the sign-in lives as long as the app).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SignInRecord? _record;
    private bool _loaded;
    private GoogleTokens? _access;

    /// <summary>Raised (on any thread) when the user signed in or out.</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    public string Provider => ProviderCode;

    /// <inheritdoc />
    public string Label => "Google";

    /// <inheritdoc />
    public async Task<bool> IsSignedInAsync(CancellationToken cancellationToken) =>
        await RecordAsync(cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>The saved sign-in (null when not signed in): for the page (project, date), never its secrets.</summary>
    public async Task<SignInRecord?> RecordAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The access of a request while signed in (null when not signed in): a cached access token, or a new one from the refresh token. A
    /// refused refresh token (expired: 7 days while the app is in "Testing", or revoked) is a <see cref="ProviderException"/> <c>auth</c>.
    /// </summary>
    public async Task<GoogleCloudAccess?> AccessAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await LoadAsync(cancellationToken).ConfigureAwait(false) is not { } record)
            {
                return null;
            }

            if (_access is { } cached && cached.ExpiresAtUtc - clock.GetUtcNow() > RenewBefore)
            {
                return new GoogleCloudAccess(cached.AccessToken, record.ProjectId);
            }

            GoogleTokens fresh;
            try
            {
                fresh = await tokens.RefreshAsync(new GoogleOAuthClient(record.ClientId, record.ClientSecret), record.RefreshToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ProviderException ex) when (ex.Status is 400 or 401)
            {
                _access = null;
                logger.LogWarning("Google sign-in: the refresh token was refused (HTTP {Status}); sign in again", ex.Status);
                throw new ProviderException("The Google sign-in has expired or was revoked; sign in again (Settings → Keys → Google Cloud).", ex)
                {
                    Code = "auth",
                    Status = ex.Status,
                };
            }

            _access = fresh;
            if (fresh.RefreshToken.Length > 0 && fresh.RefreshToken != record.RefreshToken)
            {
                record.RefreshToken = fresh.RefreshToken;
                await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation("Google sign-in: a new access token, valid until {Until:HH:mm} UTC", fresh.ExpiresAtUtc);
            return new GoogleCloudAccess(fresh.AccessToken, record.ProjectId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Signs in with the user's client for <paramref name="projectId"/>: <paramref name="showAddress"/> gets the address at once (the page
    /// shows it to copy), <paramref name="openBrowser"/> opens it (false = it could not be opened; the address stays on the page). Ends
    /// when the browser comes back, after <see cref="BrowserWait"/> (<c>timeout</c>), or on <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task SignInAsync(GoogleOAuthClient client, string projectId, Func<Uri, bool> openBrowser, Action<Uri> showAddress, LoopbackPages pages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(openBrowser);
        ArgumentNullException.ThrowIfNull(showAddress);
        var project = (projectId ?? string.Empty).Trim();
        if (project.Length == 0)
        {
            throw new GoogleSignInException("No Google Cloud project id was given.") { Code = "project" };
        }

        var verifier = Pkce.NewVerifier();
        var state = Pkce.NewState();
        using var listener = new LoopbackCodeListener(pages);
        var address = GoogleAuthorization.Url(client.ClientId, listener.RedirectUri, Pkce.Challenge(verifier), state);
        logger.LogInformation("Google sign-in: listening on 127.0.0.1 for the browser's answer");
        showAddress(address);
        if (!openBrowser(address))
        {
            logger.LogWarning("Google sign-in: the browser could not be opened; the address is shown to copy");
        }

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(BrowserWait);
        string code;
        try
        {
            code = await listener.WaitForCodeAsync(state, wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Google sign-in: no answer from the browser in {Minutes} minutes", BrowserWait.TotalMinutes);
            throw new GoogleSignInException("The browser did not come back in time.", ex) { Code = "timeout" };
        }

        var answer = await tokens.ExchangeAsync(client, code, verifier, listener.RedirectUri, cancellationToken).ConfigureAwait(false);
        if (answer.RefreshToken.Length == 0)
        {
            throw new GoogleSignInException("Google gave no refresh token.") { Code = "no_refresh" };
        }

        var record = new SignInRecord
        {
            Provider = ProviderCode,
            ClientId = client.ClientId,
            ClientSecret = client.ClientSecret,
            ProjectId = project,
            RefreshToken = answer.RefreshToken,
            SignedInAtUtc = clock.GetUtcNow(),
        };
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
            _record = record;
            _loaded = true;
            _access = answer;
        }
        finally
        {
            _gate.Release();
        }

        logger.LogInformation("Google sign-in completed");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Signs out: the sign-in is deleted here first (it is gone even when Google cannot be reached), then Google is asked to revoke the
    /// refresh token. Returns whether Google confirmed it.
    /// </summary>
    public async Task<bool> SignOutAsync(CancellationToken cancellationToken)
    {
        SignInRecord? record;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            record = await LoadAsync(cancellationToken).ConfigureAwait(false);
            await store.DeleteAsync(ProviderCode, cancellationToken).ConfigureAwait(false);
            _record = null;
            _access = null;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }

        logger.LogInformation("Google sign-in deleted on this computer");
        Changed?.Invoke(this, EventArgs.Empty);
        if (record is null || record.RefreshToken.Length == 0)
        {
            return false;
        }

        try
        {
            await tokens.RevokeAsync(record.RefreshToken, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Google sign-in revoked at Google");
            return true;
        }
        catch (ProviderException ex)
        {
            logger.LogWarning("Google sign-in: Google could not revoke it ({Code}); it is deleted here", ex.Code);
            return false;
        }
    }

    /// <summary>Attaches the access of a request: <c>Authorization: Bearer</c> and the project billed (<c>x-goog-user-project</c>).</summary>
    public static void Apply(HttpRequestMessage request, GoogleCloudAccess access)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(access);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access.AccessToken);
        request.Headers.TryAddWithoutValidation("x-goog-user-project", access.ProjectId);
    }

    private async Task<SignInRecord?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _record = await store.GetAsync(ProviderCode, cancellationToken).ConfigureAwait(false);
            _loaded = true;
        }

        return _record;
    }
}
