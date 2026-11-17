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
            "{\"konseptler\":[{\"baslik\":\"Axes of the north\",\"ozet\":\"One. Two.\",\"icerik\":\"[on screen: axe]\\nBasil II formed the guard in 988.\",\"kaynaklar\":[1]}]}",
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

    /// <summary>Answers Wikipedia and Wikidata by what is asked (the searches run side by side, so their order is not fixed).</summary>
    private sealed class Routed : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            lock (Asked)
            {
                Asked.Add(uri);
            }

            var body = uri.Contains("wbsearchentities", StringComparison.Ordinal) ? """{"search":[]}"""
                : uri.Contains("generator=search", StringComparison.Ordinal) ? Search
                : uri.Contains("titles=", StringComparison.Ordinal) ? Article
                : throw new HttpRequestException("Not a saved answer: " + uri);
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
