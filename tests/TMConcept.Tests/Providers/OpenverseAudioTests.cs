using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Music;
using TMConcept.Providers.Common;
using TMConcept.Providers.Music;

namespace TMConcept.Tests.Providers;

/// <summary>
/// The Openverse audio search (0.1.45, S6) read from a recorded answer, without the network: only CC0, CC BY and CC BY-SA results with an
/// HTTPS file stay (NC, ND, the public domain mark and a plain HTTP file are dropped and counted), each with its page, creator, licence and
/// attribution; the request addresses ask for both licence filters, anonymously.
/// </summary>
public sealed class OpenverseAudioTests
{
    private const string Answer = """
        {
          "result_count": 312, "page_count": 16, "page_size": 20, "page": 2,
          "results": [
            {"id": "aaa-1", "title": "Calm Harbour", "foreign_landing_url": "https://www.jamendo.com/track/1", "url": "https://cdn.example.org/a.mp3",
             "creator": "Ada", "license": "by", "license_version": "4.0", "attribution": "\"Calm Harbour\" by Ada is licensed under CC BY 4.0.",
             "duration": 125400, "filesize": 3000000},
            {"id": "bbb-2", "title": "Open Field", "foreign_landing_url": "https://freesound.org/s/2", "url": "https://cdn.example.org/b.ogg",
             "creator": "Bo", "license": "cc0", "license_version": "1.0", "attribution": "\"Open Field\" by Bo is marked with CC0 1.0.", "duration": 60000},
            {"id": "ccc-3", "title": "Shared Song", "url": "https://cdn.example.org/c.mp3", "creator": "Cy", "license": "by-sa", "license_version": "3.0",
             "duration": null},
            {"id": "ddd-4", "title": "No Money", "url": "https://cdn.example.org/d.mp3", "license": "by-nc", "license_version": "4.0"},
            {"id": "eee-5", "title": "No Changes", "url": "https://cdn.example.org/e.mp3", "license": "by-nd", "license_version": "4.0"},
            {"id": "fff-6", "title": "Both", "url": "https://cdn.example.org/f.mp3", "license": "by-nc-sa", "license_version": "2.0"},
            {"id": "ggg-7", "title": "Marked", "url": "https://cdn.example.org/g.mp3", "license": "pdm", "license_version": "1.0"},
            {"id": "hhh-8", "title": "Plain", "url": "http://cdn.example.org/h.mp3", "license": "by", "license_version": "4.0"}
          ]
        }
        """;

    [Fact]
    public void OnlyCommercialUseWithChangesStaysAndTheRestIsCounted()
    {
        var page = OpenverseAudioReader.Read(Answer);

        page.Tracks.Select(t => t.Id).ShouldBe(["openverse:aaa-1", "openverse:bbb-2", "openverse:ccc-3"]);
        page.Dropped.ShouldBe(5);
        (page.Page, page.PageCount).ShouldBe((2, 16));
        page.Tracks.ShouldAllBe(t => MusicLicenses.Allowed(OpenverseAudioClient.LicenseCode(t.License)));
    }

    [Fact]
    public void ATrackKeepsItsSourceCreatorLicenceAndAttribution()
    {
        var page = OpenverseAudioReader.Read(Answer);
        var by = page.Tracks[0];

        by.Title.ShouldBe("Calm Harbour");
        by.Artist.ShouldBe("Ada");
        by.PageUrl.ShouldBe("https://www.jamendo.com/track/1");
        by.DownloadUrl.ShouldBe("https://cdn.example.org/a.mp3");
        by.License.ShouldBe("CC BY 4.0");
        by.Attribution.ShouldContain("Ada");
        by.DurationSeconds.ShouldBe(125);
        by.Bytes.ShouldBe(3_000_000);

        page.Tracks[1].License.ShouldBe("CC0 1.0");
        page.Tracks[1].Attribution.ShouldBeEmpty("CC0 asks for no credit");

        // No attribution in the answer: the app writes one for a CC BY-SA track.
        page.Tracks[2].License.ShouldBe("CC BY-SA 3.0");
        page.Tracks[2].Attribution.ShouldContain("Shared Song");
        page.Tracks[2].Attribution.ShouldContain("CC BY-SA 3.0");
    }

    [Fact]
    public void TheLicenceCheckRefusesWhatTheSearchShouldNotHaveSent()
    {
        MusicLicenses.Allowed("by").ShouldBeTrue();
        MusicLicenses.Allowed("BY-SA").ShouldBeTrue();
        MusicLicenses.Allowed("cc0").ShouldBeTrue();
        foreach (var code in new[] { "by-nc", "by-nd", "by-nc-sa", "by-nc-nd", "pdm", "sampling+", string.Empty, null })
        {
            MusicLicenses.Allowed(code).ShouldBeFalse(code ?? "null");
        }

        OpenverseAudioClient.LicenseCode("CC BY-NC 4.0").ShouldBe("by-nc");
        OpenverseAudioClient.LicenseCode("CC0 1.0").ShouldBe("cc0");
        Should.Throw<InvalidDataException>(() => OpenverseAudioReader.Read("{\"detail\": \"throttled\"}"));
        Should.Throw<InvalidDataException>(() => OpenverseAudioReader.Read("not json"));
    }

    [Fact]
    public void TheRequestsAskForBothLicenceFiltersWithoutAKey()
    {
        var open = OpenverseAudioReader.Address("calm piano & sea", 3, "license_type=commercial,modification");
        var free = OpenverseAudioReader.Address("calm piano & sea", 3, "license=cc0");

        open.AbsoluteUri.ShouldStartWith("https://api.openverse.org/v1/audio/?q=calm%20piano%20%26%20sea&");
        open.Query.ShouldContain("license_type=commercial,modification");
        open.Query.ShouldContain("page=3");
        open.Query.ShouldContain("page_size=20");
        free.Query.ShouldContain("license=cc0");
        OpenverseAudioReader.Address(new string('x', 500), 1, "license=cc0").Query.Length.ShouldBeLessThan(300, "the words are cut");
    }

    [Fact]
    public async Task APagePastTheEndOfTheCc0RequestKeepsTheOtherRequestsTracks()
    {
        // Page 2 of the search: the commercial request still has results, the CC0 one has fewer pages and Openverse refuses its page 2.
        var server = new FakeOpenverse(request => request.RequestUri!.Query.Contains("license=cc0", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"detail\": \"Invalid page.\"}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Answer) });
        using var client = new OpenverseAudioClient(new ProviderTransport(() => server), NullLogger<OpenverseAudioClient>.Instance);

        var page = await client.SearchAsync("calm", 2, CancellationToken.None);

        page.Tracks.Count.ShouldBe(3);
        (page.Page, page.PageCount).ShouldBe((2, 16));
        server.Requests.ShouldBe(2);

        // Page 1 has no "past the end": a refused first page is a failed search.
        await Should.ThrowAsync<HttpRequestException>(() => client.SearchAsync("calm", 1, CancellationToken.None));
    }

    private sealed class FakeOpenverse(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            request.RequestUri!.Host.ShouldBe("api.openverse.org");
            request.Headers.Authorization.ShouldBeNull("the search is anonymous");
            return Task.FromResult(answer(request));
        }
    }
}
