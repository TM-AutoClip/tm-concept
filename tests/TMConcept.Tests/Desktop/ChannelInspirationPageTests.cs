using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Core.Channels;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Core;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The "Kanaldan esinlen" window (0.1.53) on the real profile page: the preview is filled from a fake look (nothing downloaded, nothing run),
/// "Without AI" makes no model call and "With AI" exactly one, "Profili oluştur" makes a NEW profile and a NEW template while the existing
/// profile stays as it was, a full template list only warns, and closing the window deletes the look's temporary files.
/// </summary>
public sealed class ChannelInspirationPageTests
{
    [AvaloniaFact]
    public async Task WithoutAiTheNumbersMakeANewProfileAndTemplateAndTheExistingOneStays()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        page.Name = "Existing";
        page.Language = page.Languages.First(l => l.Value == ContentLanguages.English);
        page.ConceptText = "My own channel about something else entirely, written by hand.";
        await page.SaveAsync();
        var existing = (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).Single();

        var inspirer = new FakeInspirer();
        var writer = new FakeWriter();
        var activity = new Lines();
        using var model = Model(h, inspirer, writer, activity, ai: null);
        model.Create = page.CreateFromChannelAsync;
        await model.LoadAsync();
        model.AiAvailable.ShouldBeFalse();
        model.UseAi.ShouldBeFalse("no text model: only the numbers");
        model.StartCommand.CanExecute(null).ShouldBeFalse("no address yet");

        model.Address = "https://www.youtube.com/@tarihkutusu/videos";
        model.Samples = 9;
        await model.StartCommand.ExecuteAsync(null);
        await model.Running;

        inspirer.Samples.ShouldBe(ChannelLimits.SamplesMax, "at most 5 samples");
        writer.Calls.ShouldBe(0, "without AI no model is called");
        model.HasResult.ShouldBeTrue();
        model.Steps.ShouldContain(text["inspire.stage.ready"]);
        model.Name.ShouldBe(text.Format("inspire.profile.name", "Tarih Kutusu"));
        model.Language.ShouldNotBeNull().Value.ShouldBe(ContentLanguages.Turkish);
        model.TargetSeconds.ShouldBe(95);
        model.ConceptText.ShouldContain(text["inspire.draft.todo"]);
        model.TemplatePossible.ShouldBeTrue();
        model.HasTemplateWarning.ShouldBeFalse();

        // The window shows the preview (and closes without making anything).
        var window = new TMConcept.Desktop.Views.ChannelInspirationWindow { DataContext = model };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>().Single(b => b.Name == "CreateButton").IsVisible.ShouldBeTrue();
        window.Close();

        await model.CreateProfileCommand.ExecuteAsync(null);

        var all = await h.Get<IProfileStore>().ListAsync(CancellationToken.None);
        all.Count.ShouldBe(2, "a new profile, never written over the existing one");
        var stayed = all.Single(p => p.Id == existing.Id);
        stayed.Name.ShouldBe("Existing");
        stayed.ConceptText.ShouldBe(existing.ConceptText);
        var made = all.Single(p => p.Id != existing.Id);
        made.Name.ShouldBe(model.Name);
        made.ContentLanguage.ShouldBe(ContentLanguages.Turkish);
        made.TargetSeconds.ShouldBe(95);
        h.Get<AppSession>().Profile!.Id.ShouldBe(made.Id);
        var style = (await h.Get<IVideoStyleStore>().ListAsync(CancellationToken.None)).Single();
        style.Name.ShouldBe(text.Format("inspire.template.name", "Tarih Kutusu"));
        style.Reference.ShouldNotBeNull().CutsPer10Seconds.ShouldBe(4);
        activity.Entries.ShouldContain(e => e.Level == ActivityLevel.Info);

        model.Dispose();
        inspirer.Discarded.ShouldBe(1, "closing deletes the look's temporary files");
    }

    [AvaloniaFact]
    public async Task WithAiTheModelIsCalledOnceAndAFullTemplateListOnlyWarns()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var styles = h.Get<IVideoStyleStore>();
        for (var i = 0; i < VideoStyleLimits.Max; i++)
        {
            await styles.SaveAsync(new VideoStyle { Name = "T" + i }, CancellationToken.None);
        }

        var before = (await styles.ListAsync(CancellationToken.None)).Select(s => s.Id).Order().ToList();
        var page = h.Get<ProfileViewModel>();
        var inspirer = new FakeInspirer();
        var writer = new FakeWriter();
        using var model = Model(h, inspirer, writer, new Lines(), ChannelInspirationTests.Model());
        model.Create = page.CreateFromChannelAsync;
        await model.LoadAsync();
        model.AiAvailable.ShouldBeTrue();
        model.UseAi.ShouldBeTrue();
        model.AiLabel.ShouldContain("Claude Haiku 5.5");
        model.Estimate.ShouldNotBeEmpty("the estimate shows before the call");

        model.Address = "https://www.youtube.com/@tarihkutusu";
        await model.StartCommand.ExecuteAsync(null);
        await model.Running;

        writer.Calls.ShouldBe(1, "one call");
        model.ConceptText.ShouldBe(FakeWriter.Content);
        model.Notes.ShouldContain(n => n.Contains("Claude Haiku 5.5", StringComparison.Ordinal), "the exact cost is said");
        model.TemplatePossible.ShouldBeFalse();
        model.TemplateWarning.ShouldBe(text.Format("inspire.template.limit", VideoStyleLimits.Max));

        await model.CreateProfileCommand.ExecuteAsync(null);
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).Count.ShouldBe(1);
        (await styles.ListAsync(CancellationToken.None)).Select(s => s.Id).Order().ToList().ShouldBe(before, "no template added, none changed");
    }

    [AvaloniaFact]
    public async Task AnAddressThatIsNotOneAndACancelKeepNothing()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var inspirer = new FakeInspirer { Cancel = true };
        using var model = Model(h, inspirer, new FakeWriter(), new Lines(), ai: null);
        model.Address = "not an address";
        await model.StartCommand.ExecuteAsync(null);
        await model.Running;
        model.Status.ShouldBe(text["inspire.error.address"]);
        inspirer.Looks.ShouldBe(0);

        model.Address = "https://example.com/@x";
        var start = model.StartCommand.ExecuteAsync(null);
        while (inspirer.Looks == 0)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        model.Cancel();
        await start;
        await model.Running;
        model.Status.ShouldBe(text["inspire.cancelled"]);
        model.HasResult.ShouldBeFalse();
        model.CreateProfileCommand.CanExecute(null).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task AProfileThatCannotBeMadeLeavesThePageOnTheSelectedProfile()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        page.Name = "Existing";
        page.Language = page.Languages.First(l => l.Value == ContentLanguages.English);
        page.ConceptText = "My own channel about something else entirely, written by hand.";
        await page.SaveAsync();

        // An empty channel content is refused by the page's own checks: the reason comes back, nothing is written and the page does not
        // keep an unsaved new profile (whose photo would be the look's temporary file, deleted when the window closes).
        var failure = await page.CreateFromChannelAsync(new ChannelProfileDraft("Tarih Kutusu esinli", ContentLanguages.Turkish, true, 60, string.Empty, null));
        failure.ShouldNotBeNullOrEmpty();
        page.IsNew.ShouldBeFalse();
        page.Name.ShouldBe("Existing");
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).Count.ShouldBe(1);
    }

    private static ChannelInspirationViewModel Model(DesktopHarness h, FakeInspirer inspirer, FakeWriter writer, IActivityLog activity, ResolvedModel? ai) =>
        new(new ChannelInspirationServices(inspirer, writer, h.Get<IChannelTemplateCreator>(), () => Task.FromResult(true), _ => Task.FromResult(ai), activity,
            TimeProvider.System), h.Get<Localizer>(), NullLogger<ChannelInspirationViewModel>.Instance);

    private sealed class FakeInspirer : IChannelInspirer
    {
        public bool Cancel { get; set; }

        public int Samples { get; private set; }

        public int Looks { get; private set; }

        public int Discarded { get; private set; }

        public IReadOnlyCollection<string> ActiveFolders => [];

        public bool Ready => true;

        public async Task<ChannelInspiration> InspectAsync(string address, int samples, bool photo, IProgress<ChannelProgress>? progress,
            CancellationToken cancellationToken)
        {
            Samples = samples;
            progress?.Report(new ChannelProgress(ChannelStages.List, 0, 0, 0));
            Looks++;
            if (Cancel)
            {
                // A download that runs until the window's Cancel.
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return ChannelInspirationTests.Inspiration();
        }

        public void Discard(ChannelInspiration inspiration) => Discarded++;
    }

    private sealed class FakeWriter : IChannelStyleWriter
    {
        public const string Content = "Calm narration of history for curious adults; no politics.";

        public int Calls { get; private set; }

        public CostEstimate Estimate(CatalogModel model, string language) => new(0.001m, 0.004m, 3000, 300, 1800);

        public Task<ChannelStyleResult> WriteAsync(ChannelInspiration inspiration, ResolvedModel model, string language, IProgress<ChatProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ChannelStyleResult(Content, 0.002m, true));
        }
    }

    private sealed class Lines : IActivityLog
    {
        public List<(ActivityLevel Level, string Text)> Entries { get; } = [];

        public void Add(ActivityLevel level, string text) => Entries.Add((level, text));

        public void Add(ActivityLevel level, string text, string fileText) => Entries.Add((level, text));
    }
}
