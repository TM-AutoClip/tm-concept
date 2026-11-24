using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Core.Speech;
using TMConcept.Providers.Common;
using TMConcept.Providers.GoogleAuth;
using TMConcept.Providers.Speech;

namespace TMConcept.Tests.Providers;

/// <summary>
/// 0.1.47 (C6, H3): "Sign in with Google" for Chirp 3 HD with the user's own desktop client. Google is never called: its token address is
/// a fake network; the one-time local address is asked from the test itself, on 127.0.0.1 only.
/// </summary>
public sealed class GoogleSignInTests : IDisposable
{
    private const string ClientFile = """{"installed":{"client_id":"123-abc.apps.googleusercontent.com","project_id":"p","auth_uri":"https://accounts.google.com/o/oauth2/auth","token_uri":"https://oauth2.googleapis.com/token","client_secret":"fake-client-secret-0123","redirect_uris":["http://localhost"]}}""";

    private static readonly GoogleOAuthClient Client = new("123-abc.apps.googleusercontent.com", "fake-client-secret-0123");

    private readonly string _folder = TestFolders.New("google-");
    private readonly HttpClient _local = new() { Timeout = TimeSpan.FromSeconds(10) };

    public void Dispose()
    {
        _local.Dispose();
        TestFolders.Delete(_folder);
    }

    [Fact]
    public void ThePkceChallengeIsTheS256OfTheVerifier()
    {
        // RFC 7636, appendix B.
        Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk").ShouldBe("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
        var verifier = Pkce.NewVerifier();
        verifier.Length.ShouldBe(43);
        verifier.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
        Pkce.NewVerifier().ShouldNotBe(verifier);
        Pkce.NewState().ShouldNotBe(Pkce.NewState());
        Pkce.SameState("abc", "abc").ShouldBeTrue();
        Pkce.SameState("abc", "abd").ShouldBeFalse();
        Pkce.SameState("abc", null).ShouldBeFalse();
    }

    [Fact]
    public void TheAuthorizationAddressAsksForACodeWithPkceOfflineAccessAndTheCloudScope()
    {
        var url = GoogleAuthorization.Url(Client.ClientId, new Uri("http://127.0.0.1:50123/"), "challenge-value", "state-value");

        url.GetLeftPart(UriPartial.Path).ShouldBe("https://accounts.google.com/o/oauth2/v2/auth");
        var query = LoopbackCodeListener.Query(url.PathAndQuery);
        query["client_id"].ShouldBe(Client.ClientId);
        query["redirect_uri"].ShouldBe("http://127.0.0.1:50123/");
        query["response_type"].ShouldBe("code");
        query["scope"].ShouldBe("https://www.googleapis.com/auth/cloud-platform");
        query["code_challenge"].ShouldBe("challenge-value");
        query["code_challenge_method"].ShouldBe("S256");
        query["state"].ShouldBe("state-value");
        query["access_type"].ShouldBe("offline");
        query["prompt"].ShouldBe("consent");
        query.ShouldNotContainKey("client_secret");
    }

    [Fact]
    public async Task TheListenerTakesOneCodeAndCloses()
    {
        var listener = new LoopbackCodeListener(new LoopbackPages("done-page", "failed-page"));
        using var _ = listener;
        listener.RedirectUri.Host.ShouldBe("127.0.0.1");
        var waiting = listener.WaitForCodeAsync("s-1", CancellationToken.None);

        // The browser's icon is not the answer: 404 and the listener waits on.
        (await _local.GetAsync(new Uri(listener.RedirectUri, "favicon.ico"), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        waiting.IsCompleted.ShouldBeFalse();

        var answer = await _local.GetAsync(new Uri(listener.RedirectUri, "?state=s-1&code=4%2Fabc-def&scope=x"), TestContext.Current.CancellationToken);
        answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("done-page");
        (await waiting).ShouldBe("4/abc-def");
        listener.Stopped.ShouldBeTrue();
        await Should.ThrowAsync<HttpRequestException>(() => _local.GetAsync(new Uri(listener.RedirectUri, "?state=s-1&code=again"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAnswerWithAnotherStateIsRefused()
    {
        using var listener = new LoopbackCodeListener();
        var waiting = listener.WaitForCodeAsync("right-state", CancellationToken.None);

        var answer = await _local.GetAsync(new Uri(listener.RedirectUri, "?state=wrong-state&code=stolen"), TestContext.Current.CancellationToken);

        answer.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Should.ThrowAsync<GoogleSignInException>(() => waiting)).Code.ShouldBe("state");
        listener.Stopped.ShouldBeTrue();
    }

    [Fact]
    public async Task ARefusalInTheBrowserAndACancelEndTheWait()
    {
        using (var listener = new LoopbackCodeListener())
        {
            var waiting = listener.WaitForCodeAsync("s", CancellationToken.None);
            await _local.GetAsync(new Uri(listener.RedirectUri, "?error=access_denied&state=s"), TestContext.Current.CancellationToken);
            (await Should.ThrowAsync<GoogleSignInException>(() => waiting)).Code.ShouldBe("denied");
        }

        // A refusal with another state (or none) is not this sign-in's: it is refused as a wrong state, not believed.
        using (var listener = new LoopbackCodeListener())
        {
            var waiting = listener.WaitForCodeAsync("s", CancellationToken.None);
            (await _local.GetAsync(new Uri(listener.RedirectUri, "?error=access_denied&state=other"), TestContext.Current.CancellationToken))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Should.ThrowAsync<GoogleSignInException>(() => waiting)).Code.ShouldBe("state");
        }

        using (var listener = new LoopbackCodeListener())
        {
            using var cancel = new CancellationTokenSource();
            var waiting = listener.WaitForCodeAsync("s", cancel.Token);
            await cancel.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => waiting);
            listener.Stopped.ShouldBeTrue();
        }
    }

    [Fact]
    public void ADesktopClientFileIsReadAndOtherFilesAreRefusedWithACode()
    {
        GoogleOAuthClientFile.Parse(ClientFile).ShouldBe(Client);
        Code("""{"web":{"client_id":"x","client_secret":"y"}}""").ShouldBe("file.web");
        Code("""{"type":"service_account","private_key":"z"}""").ShouldBe("file.kind");
        Code("""{"installed":{"client_id":"x"}}""").ShouldBe("file.fields");
        Code("{\"installed\":").ShouldBe("file.json");
        Code("[1,2]").ShouldBe("file.json");

        static string Code(string json) => Should.Throw<GoogleSignInException>(() => GoogleOAuthClientFile.Parse(json)).Code;
    }

    [Fact]
    public async Task TheAccessTokenIsRenewedShortlyBeforeItEndsAndAnExpiredSignInSaysSignInAgain()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, """{"access_token":"access-1","expires_in":3600,"token_type":"Bearer","scope":"https://www.googleapis.com/auth/cloud-platform"}""")
            .Answer(HttpStatusCode.OK, """{"access_token":"access-2","expires_in":3600,"token_type":"Bearer"}""")
            .Answer(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");
        using var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
        var store = new MemorySignIns();
        await store.SaveAsync(Record("refresh-1"), CancellationToken.None);
        var signIn = new GoogleCloudSignIn(store, new GoogleTokenClient(http, clock), clock, NullLogger<GoogleCloudSignIn>.Instance);

        (await signIn.AccessAsync(CancellationToken.None)).ShouldBe(new GoogleCloudAccess("access-1", "my-project"));
        clock.Advance(TimeSpan.FromMinutes(50));
        (await signIn.AccessAsync(CancellationToken.None))!.AccessToken.ShouldBe("access-1", "still valid: no request");
        fake.Requests.Count.ShouldBe(1);
        var form = Form(fake.Requests[0].Body);
        fake.Requests[0].Uri.ShouldBe(GoogleAuthorization.TokenEndpoint);
        form["grant_type"].ShouldBe("refresh_token");
        form["refresh_token"].ShouldBe("refresh-1");
        form["client_id"].ShouldBe(Client.ClientId);
        form["client_secret"].ShouldBe(Client.ClientSecret);

        clock.Advance(TimeSpan.FromMinutes(9));
        (await signIn.AccessAsync(CancellationToken.None))!.AccessToken.ShouldBe("access-2", "a minute before its end it is renewed");
        fake.Requests.Count.ShouldBe(2);

        clock.Advance(TimeSpan.FromHours(2));
        var expired = await Should.ThrowAsync<ProviderException>(() => signIn.AccessAsync(CancellationToken.None));
        expired.Code.ShouldBe("auth");
        expired.Message.ShouldNotContain("refresh-1");
        (await signIn.IsSignedInAsync(CancellationToken.None)).ShouldBeTrue("the sign-in is kept until the user signs in again or out");
    }

    [Fact]
    public async Task NotSignedInMeansNoAccessAndNoRequest()
    {
        var fake = new FakeHttp();
        using var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var clock = new FakeTimeProvider();
        var signIn = new GoogleCloudSignIn(new MemorySignIns(), new GoogleTokenClient(http, clock), clock, NullLogger<GoogleCloudSignIn>.Instance);

        (await signIn.AccessAsync(CancellationToken.None)).ShouldBeNull();
        (await signIn.IsSignedInAsync(CancellationToken.None)).ShouldBeFalse();
        fake.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASignInGoesThroughTheLocalAddressAndKeepsTheRefreshTokenAndSignOutRevokesIt()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, """{"access_token":"access-1","expires_in":3599,"refresh_token":"refresh-new","token_type":"Bearer"}""")
            .Answer(HttpStatusCode.OK, "{}");
        using var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
        var store = new MemorySignIns();
        var signIn = new GoogleCloudSignIn(store, new GoogleTokenClient(http, clock), clock, NullLogger<GoogleCloudSignIn>.Instance);
        Uri? shown = null;
        Task<HttpResponseMessage>? browser = null;

        // The "browser": asks the local address with the state of the authorization address, as Google's redirect would.
        await signIn.SignInAsync(Client, " my-project ", address =>
        {
            var query = LoopbackCodeListener.Query(address.PathAndQuery);
            browser = _local.GetAsync(new Uri(query["redirect_uri"] + "?state=" + Uri.EscapeDataString(query["state"]) + "&code=the-code"), TestContext.Current.CancellationToken);
            return true;
        }, address => shown = address, LoopbackPages.English, CancellationToken.None);

        shown.ShouldNotBeNull();
        (await browser!).StatusCode.ShouldBe(HttpStatusCode.OK);
        var shownQuery = LoopbackCodeListener.Query(shown!.PathAndQuery);
        var exchange = Form(fake.Requests[0].Body);
        exchange["grant_type"].ShouldBe("authorization_code");
        exchange["code"].ShouldBe("the-code");
        exchange["redirect_uri"].ShouldBe(shownQuery["redirect_uri"], "the same redirect address");
        Pkce.Challenge(exchange["code_verifier"]).ShouldBe(shownQuery["code_challenge"]);
        var saved = await store.GetAsync(GoogleCloudSignIn.ProviderCode, CancellationToken.None);
        saved!.RefreshToken.ShouldBe("refresh-new");
        saved.ProjectId.ShouldBe("my-project");
        saved.ClientSecret.ShouldBe(Client.ClientSecret);
        (await signIn.AccessAsync(CancellationToken.None))!.AccessToken.ShouldBe("access-1", "the first access token is used, no refresh yet");

        (await signIn.SignOutAsync(CancellationToken.None)).ShouldBeTrue();
        (await store.GetAsync(GoogleCloudSignIn.ProviderCode, CancellationToken.None)).ShouldBeNull();
        fake.Requests[1].Uri.ShouldBe(GoogleAuthorization.RevokeEndpoint);
        Form(fake.Requests[1].Body)["token"].ShouldBe("refresh-new");
        (await signIn.AccessAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ASignOutWithoutGoogleStillDeletesTheSignInHere()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.InternalServerError, "{}");
        using var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var clock = new FakeTimeProvider();
        var store = new MemorySignIns();
        await store.SaveAsync(Record("refresh-1"), CancellationToken.None);
        var signIn = new GoogleCloudSignIn(store, new GoogleTokenClient(http, clock), clock, NullLogger<GoogleCloudSignIn>.Instance);

        (await signIn.SignOutAsync(CancellationToken.None)).ShouldBeFalse();
        (await signIn.IsSignedInAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task ChirpWithASignInSendsBearerAndTheBilledProjectInsteadOfTheKey()
    {
        var wave = Wave(2400);
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, """{"access_token":"access-1","expires_in":3600}""")
            .Answer(HttpStatusCode.OK, new JsonObject { ["audioContent"] = Convert.ToBase64String(wave) }.ToJsonString());
        using var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var clock = new FakeTimeProvider();
        var store = new MemorySignIns();
        await store.SaveAsync(Record("refresh-1"), CancellationToken.None);
        var signIn = new GoogleCloudSignIn(store, new GoogleTokenClient(http, clock), clock, NullLogger<GoogleCloudSignIn>.Instance);
        var engine = new GoogleCloudSpeechEngine(new GoogleCloudProvider(http, NullLogger<GoogleCloudProvider>.Instance), http, signIn);
        var model = new ResolvedModel(new CatalogModel { Provider = "google-cloud", Id = "chirp3-hd", Name = "c", Capabilities = ModelCapability.Speech },
            new ApiKey { Provider = "google-cloud", Label = "Google", Secret = "fake-api-key-not-used-0123" }, string.Empty);

        await engine.SynthesizeAsync(new SpeechRequest("Bir.", "tr", "Achernar", 1, Path.Combine(_folder, "c.wav"), model), CancellationToken.None);

        var speak = fake.Requests[1];
        speak.Uri.ToString().ShouldBe("https://texttospeech.googleapis.com/v1/text:synthesize");
        speak.Headers["Authorization"].ShouldBe("Bearer access-1");
        speak.Headers["x-goog-user-project"].ShouldBe("my-project");
        speak.Headers.ShouldNotContainKey("x-goog-api-key", "the sign-in comes before the key");
    }

    private static SignInRecord Record(string refresh) => new()
    {
        Provider = GoogleCloudSignIn.ProviderCode,
        ClientId = Client.ClientId,
        ClientSecret = Client.ClientSecret,
        ProjectId = "my-project",
        RefreshToken = refresh,
    };

    private static Dictionary<string, string> Form(string body) => LoopbackCodeListener.Query("/?" + body);

    private byte[] Wave(int samples)
    {
        var path = Path.Combine(_folder, "answer.wav");
        WaveFile.WritePcm16(path, new float[samples], 24000);
        return File.ReadAllBytes(path);
    }

    private sealed class MemorySignIns : ISignInStore
    {
        private readonly Dictionary<string, SignInRecord> _records = [];

        public Task<SignInRecord?> GetAsync(string provider, CancellationToken cancellationToken) => Task.FromResult(_records.GetValueOrDefault(provider));

        public Task SaveAsync(SignInRecord record, CancellationToken cancellationToken)
        {
            _records[record.Provider] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string provider, CancellationToken cancellationToken)
        {
            _records.Remove(provider);
            return Task.CompletedTask;
        }
    }
}
