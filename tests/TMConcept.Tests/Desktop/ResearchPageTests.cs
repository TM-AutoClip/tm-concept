using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Documents;
using TMConcept.Core.Keys;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.22, "Araştırmadan" on the concept page: Search only shows the pages (nothing is fetched, no model is called), the button fetches
/// the ticked pages into "Belgelerim" and writes grounded concepts that join the one list. Saved answers and a fake model only.
/// </summary>
public sealed class ResearchPageTests
{
    private const string Secret = "fake-test-key-0123456789abcdef";

    private const string Search = """
        {"query":{"pages":[{"pageid":7,"ns":0,"title":"Varangian Guard","index":1,"pageprops":{"wikibase_item":"Q838185"},
          "fullurl":"https://en.wikipedia.org/wiki/Varangian_Guard","description":"Byzantine guard unit"}]}}
        """;

    private const string TwoPages = """
        {"query":{"pages":[{"pageid":7,"ns":0,"title":"Varangian Guard","index":1,"pageprops":{"wikibase_item":"Q838185"},
          "fullurl":"https://en.wikipedia.org/wiki/Varangian_Guard"},
          {"pageid":8,"ns":0,"title":"Byzantine army","index":2,"pageprops":{"wikibase_item":"Q1"},"fullurl":"https://en.wikipedia.org/wiki/Byzantine_army"}]}}
        """;

    private const string Article = """
        {"query":{"pages":[{"pageid":7,"ns":0,"title":"Varangian Guard","fullurl":"https://en.wikipedia.org/wiki/Varangian_Guard",
          "extract":"The Varangian Guard was an elite unit of the Byzantine army.\n\n== History ==\nBasil II formed it in 988."}]}}
        """;

    private static readonly CancellationToken None = CancellationToken.None;

    [AvaloniaFact]
    public async Task SearchShowsThePagesAndTheButtonFetchesThemAndWritesGroundedConceptsIntoTheOneList()
    {
        var web = new Routed();
        var fake = new FakeChat().Concepts(["Greek fire"]).Answer(_ => new ChatResponse(
            "{\"konseptler\":[{\"baslik\":\"Axes of the north\",\"ozet\":\"One. Two.\"," + FakeChat.SceneList("Basil II formed the guard in 988") + ",\"kaynaklar\":[1]}]}",
            new ChatUsage(1000, 0, 0, 800), null, "end_turn", "r"));
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton(new TMConcept.Documents.Web.DocumentTransport(() => web));
        });
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = Secret }, None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        var text = h.Get<Localizer>();

        page.Ideas.Count = 1;
        await page.Ideas.GenerateAsync();
        page.Research.Blocked.ShouldBe(text["research.search_first"]);
        page.Research.Subject = "Varangian Guard";
        page.Research.Count = 1;
        await page.Research.SearchAsync();

        page.Research.Candidates.Select(c => (c.Title, c.Source, c.Checked)).ShouldBe([("Varangian Guard", "Wikipedia · en", true)]);
        page.Research.PlanLine.ShouldBe(text.Format("research.plan", "en", "Wikipedia, Wikidata"));
        web.Asked.ShouldAllBe(u => u.Contains("search", StringComparison.Ordinal), "a search fetches no page");
        fake.Requests.Count.ShouldBe(1, "and calls no model");
        page.Documents.Library.Documents.ShouldBeEmpty();

        page.Research.GenerateText.ShouldBe(text.Format("research.generate", 1, 1));
        await page.Research.GenerateAsync();

        page.Research.Status.ShouldNotBeEmpty();
        page.Research.StatusIsError.ShouldBeFalse(page.Research.Status);
        page.NewConcepts.Select(c => c.Concept.Title).ShouldBe(["Axes of the north", "Greek fire"], "the research concept joins the same list");
        var document = page.Documents.Library.Documents.ShouldHaveSingleItem().Document;
        (document.Kind, document.Title, document.Language).ShouldBe((DocumentKind.Wikipedia, "Varangian Guard", "en"));
        var request = fake.Requests[1];
        request.System.ShouldContain("Use ONLY the facts in the DOCUMENT PASSAGES");
        request.User.ShouldContain("SUBJECT: «Varangian Guard»");
        request.User.ShouldContain("Basil II formed it in 988.");
        var saved = await h.Get<IConceptStore>().ListAsync(profile.Id, [ConceptStatus.New], false, None);
        saved.Single(c => c.Title == "Axes of the north").Sources.ShouldBe("Varangian Guard");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACancelWhileFetchingTakesAwayThePagesItAddedUnlessTheyAreKeptAndTheListsShowIt(bool keep)
    {
        ResearchSectionViewModel? section = null;
        var articles = 0;
        var web = new Routed(TwoPages, () =>
        {
            if (Interlocked.Increment(ref articles) == 2)
            {
                section!.Cancel();
            }
        });
        var (h, page, fake) = await OpenAsync(web);
        using (h)
        {
            var text = h.Get<Localizer>();
            section = page.Research;
            section.Subject = "Varangian Guard";
            section.Count = 1;
            section.KeepInLibrary = keep;
            await section.SearchAsync();
            section.CheckedCount.ShouldBe(2);

            await section.GenerateAsync();

            articles.ShouldBe(2);
            fake.Requests.ShouldBeEmpty("a canceled run calls no model");
            section.StatusIsWarning.ShouldBeTrue(section.Status);
            section.Status.ShouldBe(text[keep ? "research.fetch_canceled" : "research.fetch_canceled_dropped"]);
            var stored = await h.Get<IDocumentStore>().ListAsync(h.Get<AppSession>().Profile!.Id, None);
            stored.Count.ShouldBe(keep ? 1 : 0, "the page saved before the cancel stays only when it is kept");
            page.Documents.Library.Documents.Count.ShouldBe(stored.Count, "My documents shows what is stored");
            section.Running.ShouldBeFalse();
            section.CanSearch.ShouldBeTrue("the section is free again");
        }
    }

    [AvaloniaFact]
    public async Task TheButtonWaitsForANewSearchWhenTheSubjectChangesAndForAtMostPagesTickedPages()
    {
        var (h, page, _) = await OpenAsync(new Routed(TwoPages));
        using (h)
        {
            var text = h.Get<Localizer>();
            var section = page.Research;
            section.Subject = "Varangian Guard";
            section.Count = 1;
            await section.SearchAsync();
            section.Blocked.ShouldBeEmpty();

            section.Subject = "Ottoman navy";
            section.Blocked.ShouldBe(text["research.search_again"], "the pages found are about the old subject");
            section.Subject = "Varangian Guard ";
            section.Blocked.ShouldBeEmpty();
            section.Languages = "fr";
            section.Blocked.ShouldBe(text["research.search_again"]);
            section.Languages = string.Empty;
            section.Urls = "https://example.org/notes";
            section.Blocked.ShouldBe(text["research.search_again"]);
            section.Urls = string.Empty;

            section.Pages = 1;
            section.Blocked.ShouldBe(text.Format("research.too_many", 1), "two pages are ticked");
            section.Candidates[1].Checked = false;
            section.Blocked.ShouldBeEmpty();
        }
    }

    private static async Task<(DesktopHarness Harness, ConceptsViewModel Page, FakeChat Fake)> OpenAsync(Routed web)
    {
        var fake = new FakeChat();
        var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton(new TMConcept.Documents.Web.DocumentTransport(() => web));
        });
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = Secret }, None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        return (h, page, fake);
    }

    /// <summary>
    /// Answers Wikipedia and Wikidata by what is asked (the searches run side by side, so their order is not fixed); <paramref name="article"/>
    /// runs when an article is asked.
    /// </summary>
    private sealed class Routed(string search = Search, Action? article = null) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            lock (Asked)
            {
                Asked.Add(uri);
            }

            if (uri.Contains("titles=", StringComparison.Ordinal))
            {
                article?.Invoke();
            }

            var body = uri.Contains("wbsearchentities", StringComparison.Ordinal) ? """{"search":[]}"""
                : uri.Contains("generator=search", StringComparison.Ordinal) ? search
                : uri.Contains("titles=", StringComparison.Ordinal) ? Article
                : throw new HttpRequestException("Not a saved answer: " + uri);
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
