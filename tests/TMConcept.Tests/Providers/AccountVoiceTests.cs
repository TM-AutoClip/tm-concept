using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Speech;
using TMConcept.Providers.Common;
using TMConcept.Providers.Speech;

namespace TMConcept.Tests.Providers;

/// <summary>
/// 0.1.47 (C6, H4): the voices of the ElevenLabs account, read page by page from recorded answers of <c>GET v2/voices</c> (no real request
/// is ever made), listed by the output language (rule 11) next to the fixed voices, and the fixed list when the account cannot be read.
/// </summary>
public sealed class AccountVoiceTests
{
    private const string Key = "fake-test-key-elevenlabs-0123";

    // Recorded in the shape of the API reference (24 Nov 2026): a premade voice verified in English, a clone without a language, a library
    // voice verified in German and Turkish, a voice with only a language label, and one with an id the speech address must not take.
    private const string FirstPage = """
        {"voices":[
          {"voice_id":"21m00Tcm4TlvDq8ikWAM","name":"Rachel","category":"premade","labels":{"accent":"american"},
           "verified_languages":[{"language":"en","model_id":"eleven_multilingual_v2","accent":"american","locale":"en-US"}]},
          {"voice_id":"CloneVoice0000000001","name":"Benim Sesim","category":"cloned","labels":{},"verified_languages":[]},
          {"voice_id":"LibraryVoice00000002","name":"Anna","category":"professional",
           "verified_languages":[{"language":"de","model_id":"eleven_v4","locale":"de-DE"},{"language":"tr","model_id":"eleven_v4","locale":"tr-TR"}]}
        ],"has_more":true,"total_count":5,"next_page_token":"page-2-token"}
        """;

    private const string SecondPage = """
        {"voices":[
          {"voice_id":"LabelVoice0000000003","name":"Pierre","category":"generated","labels":{"language":"fr"}},
          {"voice_id":"bad/id?x=1","name":"Broken","category":"cloned"}
        ],"has_more":false,"total_count":5,"next_page_token":null}
        """;

    private static ApiKey ElevenKey => new() { Provider = "elevenlabs", Label = "k", Secret = Key, LastOkAtUtc = DateTimeOffset.UnixEpoch };

    private static (ElevenLabsVoiceSource Source, ElevenLabsProvider Provider, ProviderHttp Http) Source(FakeHttp fake)
    {
        var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var provider = new ElevenLabsProvider(http, NullLogger<ElevenLabsProvider>.Instance);
        return (new ElevenLabsVoiceSource(provider, http), provider, http);
    }

    [Fact]
    public async Task EveryPageIsReadWithTheKeyAndTheNextPageToken()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, FirstPage).Answer(HttpStatusCode.OK, SecondPage);
        var (source, _, http) = Source(fake);
        using var _ = http;

        var voices = await source.FetchAsync(ElevenKey, CancellationToken.None);

        voices.Select(v => v.Id).ShouldBe(["21m00Tcm4TlvDq8ikWAM", "CloneVoice0000000001", "LibraryVoice00000002", "LabelVoice0000000003"],
            "an id the speech address cannot take is left out");
        fake.Requests.Count.ShouldBe(2);
        fake.Requests[0].Uri.ToString().ShouldBe("https://api.elevenlabs.io/v2/voices?page_size=100");
        fake.Requests[1].Uri.ToString().ShouldBe("https://api.elevenlabs.io/v2/voices?page_size=100&next_page_token=page-2-token");
        fake.Requests.ShouldAllBe(r => r.Headers["xi-api-key"] == Key && r.Method == HttpMethod.Get);
        voices[0].Languages.ShouldBe(["en"]);
        voices[1].Languages.ShouldBeEmpty("a clone says nothing about its language");
        voices[2].Languages.ShouldBe(["de", "tr"]);
        voices[3].Languages.ShouldBe(["fr"], "the label is used when nothing is verified");
        voices[1].Category.ShouldBe("cloned");
    }

    [Fact]
    public void APageWithoutAListIsRefusedAndTheLastPageHasNoNextToken()
    {
        var (_, next) = ElevenLabsVoiceSource.ParsePage(Encoding.UTF8.GetBytes(SecondPage));
        next.ShouldBeNull();
        var (_, more) = ElevenLabsVoiceSource.ParsePage(Encoding.UTF8.GetBytes(FirstPage));
        more.ShouldBe("page-2-token");
        Should.Throw<TMConcept.Core.Providers.ProviderException>(() => ElevenLabsVoiceSource.ParsePage(Encoding.UTF8.GetBytes("{\"detail\":\"x\"}")));
        Should.Throw<TMConcept.Core.Providers.ProviderException>(() => ElevenLabsVoiceSource.ParsePage(Encoding.UTF8.GetBytes("<html>")));
    }

    [Fact]
    public void AVoiceIsListedOnlyInALanguageItKnowsOrWhenItsLanguageIsUnknown()
    {
        var voices = ElevenLabsVoiceSource.ParsePage(Encoding.UTF8.GetBytes(FirstPage)).Voices
            .Concat(ElevenLabsVoiceSource.ParsePage(Encoding.UTF8.GetBytes(SecondPage)).Voices).ToList();

        var turkish = ElevenLabsSpeechEngine.Merge(ElevenLabsSpeechEngine.Premade, voices, "tr");
        turkish.Take(ElevenLabsSpeechEngine.Premade.Count).ShouldBe(ElevenLabsSpeechEngine.Premade, "the fixed voices come first, always");
        turkish.Skip(ElevenLabsSpeechEngine.Premade.Count).Select(v => v.Id).ShouldBe(["LibraryVoice00000002", "CloneVoice0000000001"],
            "Anna is verified in Turkish, the clone's language is unknown; Pierre speaks French only; Rachel is already a fixed voice");
        turkish.Single(v => v.Id == "CloneVoice0000000001").Label.ShouldBe("Benim Sesim (cloned)");

        var french = ElevenLabsSpeechEngine.Merge(ElevenLabsSpeechEngine.Premade, voices, "fr").Select(v => v.Id).ToList();
        french.ShouldContain("LabelVoice0000000003");
        french.ShouldNotContain("LibraryVoice00000002");

        AccountVoiceFilter.Speaks(new AccountVoice("a", "a", "", ["en"]), "en").ShouldBeTrue();
        AccountVoiceFilter.Speaks(new AccountVoice("a", "a", "", ["en-US"]), "en").ShouldBeTrue("a locale counts by its language");
        AccountVoiceFilter.Speaks(new AccountVoice("a", "a", "", ["en"]), "tr").ShouldBeFalse();
        AccountVoiceFilter.Speaks(new AccountVoice("a", "a", "", []), "tr").ShouldBeTrue();
    }

    [Fact]
    public async Task AnAccountThatCannotBeReadLeavesTheFixedVoicesAndSaysSo()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.Unauthorized, "{\"detail\":{\"status\":\"invalid_api_key\"}}");
        var (source, provider, http) = Source(fake);
        using var _ = http;
        var cache = new MemoryCache();
        var log = new TMConcept.Tests.Documents.RecordingLogger<AccountVoiceLibrary>();
        var library = new AccountVoiceLibrary(cache, [source], new MemoryKeys(ElevenKey), new FakeTimeProvider(), log);
        var engine = new ElevenLabsSpeechEngine(provider, http, library);
        await library.LoadAsync(CancellationToken.None);

        var result = await library.RefreshAsync("elevenlabs", CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Code.ShouldBe("auth");
        engine.Voices("tr").ShouldBe(ElevenLabsSpeechEngine.Premade, "nothing was read: the fixed list");
        cache.Saved.ShouldBeEmpty("a failed read saves nothing");
        log.Lines.ShouldContain(l => l.StartsWith("Warning:", StringComparison.Ordinal) && l.Contains("could not be read", StringComparison.Ordinal),
            "a warning reaches the activity panel");
        log.All.ShouldNotContain(Key);
    }

    [Fact]
    public async Task TheSavedListIsShownFirstAndAReadReplacesIt()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, SecondPage).Answer(HttpStatusCode.InternalServerError, "{}");
        var (source, provider, http) = Source(fake);
        using var _ = http;
        var cache = new MemoryCache();
        cache.Saved["elevenlabs"] = [new AccountVoice("SavedVoice0000000009", "Kayıtlı", "cloned", [])];
        var clock = new FakeTimeProvider();
        var library = new AccountVoiceLibrary(cache, [source], new MemoryKeys(ElevenKey), clock, NullLogger<AccountVoiceLibrary>.Instance);
        var engine = new ElevenLabsSpeechEngine(provider, http, library);

        engine.Voices("tr").ShouldBe(ElevenLabsSpeechEngine.Premade, "nothing is read before a picker asks");
        await library.LoadAsync(CancellationToken.None);
        engine.Voices("tr").Select(v => v.Id).ShouldContain("SavedVoice0000000009", "the saved list shows at once, without a request");
        fake.Requests.ShouldBeEmpty();
        library.IsDue("elevenlabs").ShouldBeTrue();

        (await library.RefreshAsync("elevenlabs", CancellationToken.None)).Count.ShouldBe(1);
        engine.Voices("fr").Select(v => v.Id).ShouldContain("LabelVoice0000000003");
        engine.Voices("fr").Select(v => v.Id).ShouldNotContain("SavedVoice0000000009");
        cache.Saved["elevenlabs"].Single().Id.ShouldBe("LabelVoice0000000003");
        library.IsDue("elevenlabs").ShouldBeFalse();
        clock.Advance(AccountVoiceLibrary.RefreshEvery);
        library.IsDue("elevenlabs").ShouldBeTrue();

        // A later failure keeps the list read last.
        (await library.RefreshAsync("elevenlabs", CancellationToken.None)).Ok.ShouldBeFalse();
        engine.Voices("fr").Select(v => v.Id).ShouldContain("LabelVoice0000000003");
    }

    [Fact]
    public async Task WithoutAKeyNothingIsAsked()
    {
        var fake = new FakeHttp();
        var (source, _, http) = Source(fake);
        using var _ = http;
        var library = new AccountVoiceLibrary(new MemoryCache(), [source], new MemoryKeys(), new FakeTimeProvider(), NullLogger<AccountVoiceLibrary>.Instance);

        (await library.RefreshAsync("elevenlabs", CancellationToken.None)).Code.ShouldBe("key");
        fake.Requests.ShouldBeEmpty();
    }

    private sealed class MemoryCache : IAccountVoiceCache
    {
        public Dictionary<string, IReadOnlyList<AccountVoice>> Saved { get; } = [];

        public Task<IReadOnlyList<AccountVoice>?> GetAsync(string provider, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.GetValueOrDefault(provider));

        public Task SaveAsync(string provider, IReadOnlyList<AccountVoice> voices, CancellationToken cancellationToken)
        {
            Saved[provider] = voices;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryKeys(params ApiKey[] keys) : IApiKeyStore
    {
        public Task<IReadOnlyList<ApiKey>> ListAsync(string? provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApiKey>>([.. keys.Where(k => provider is null || k.Provider == provider)]);

        public Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(keys.FirstOrDefault(k => k.Id == id));

        public Task SaveAsync(ApiKey key, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkOkAsync(Guid id, DateTimeOffset atUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
