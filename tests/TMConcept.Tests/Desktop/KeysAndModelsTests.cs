using System.Net;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Hardware;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>API keys, the key gate, and the models chosen on the pages (kept per profile; nothing about models in Settings).</summary>
public sealed class KeysAndModelsTests
{
    private const string Secret = "fake-test-key-0123456789abcdef";

    [AvaloniaFact]
    public async Task AKeyIsSavedTestedAndShownMasked()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """{"data":[]}""");
        using var h = new DesktopHarness(s => s.AddSingleton(fake.Transport));
        await FirstRunTests.CreateAsync(h);
        var keys = h.Get<KeysSettingsViewModel>();
        await keys.ActivateAsync();
        keys.Provider = keys.Providers.First(p => p.Provider.Code == "anthropic");
        keys.Secret = Secret;

        await keys.AddAsync();

        var row = keys.Keys.Single();
        row.Masked.ShouldNotContain("0123456789abcdef");
        row.Failed.ShouldBeFalse();
        fake.Requests.Single().Uri.Host.ShouldBe("api.anthropic.com");
        (await h.Get<IApiKeyStore>().ListAsync("anthropic", CancellationToken.None)).Single().LastOkAtUtc.ShouldNotBeNull();
        keys.Secret.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task SettingsHasNoModelChoiceAnyMore()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var settings = h.Get<SettingsViewModel>();

        await settings.ActivateAsync();

        settings.Sections.Select(s => s.TitleKey).ShouldNotContain("settings.models");
        settings.Sections.Select(s => s.TitleKey).ShouldNotContain("settings.tools");
        h.Get<Localizer>().Strings.Keys.ShouldNotContain("settings.models");
        var types = typeof(SettingsViewModel).Assembly.GetTypes().Select(t => t.Name).ToList();
        types.ShouldNotContain("ModelsSettingsViewModel");
        types.ShouldNotContain("ToolsSettingsViewModel");
        foreach (var section in settings.Sections)
        {
            section.GetType().GetProperties().Select(p => p.PropertyType).ShouldNotContain(typeof(StepPickerViewModel), section.GetType().Name);
        }
    }

    [AvaloniaFact]
    public async Task ANewProfileStartsWithNothingChosenAndEachProfileGetsItsLastChoiceBack()
    {
        var fake = new FakeChat().Concepts(FakeChat.Distinct(3));
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton<IGpuInfo>(new FakeGpu(null));
        });
        await FirstRunTests.CreateAsync(h);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "k", Secret = Secret }, CancellationToken.None);
        var session = h.Get<AppSession>();
        var first = new Profile { Name = "First", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        var second = new Profile { Name = "Second", ContentLanguage = ContentLanguages.English, ConceptText = "y" };
        await h.Get<IProfileStore>().SaveAsync(first, CancellationToken.None);
        await h.Get<IProfileStore>().SaveAsync(second, CancellationToken.None);
        await session.SelectProfileAsync(first, CancellationToken.None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();

        page.Settings.Picker.Rows.Select(r => r.Step.Code).ShouldBe(PipelineSteps.ConceptPage);
        page.Settings.Picker.Rows.ShouldAllBe(r => r.Selected!.Model == null, "nothing paid is chosen for a new profile");
        page.Settings.Picker.Rows.ShouldAllBe(r => r.Recommended.Length > 0);
        var concepts = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        page.Ideas.Count = 3;
        await page.Ideas.GenerateAsync();
        fake.Requests.Count.ShouldBe(1);

        // Saved when the process started: the other profile has its own (none), the first one gets its model back.
        await session.SelectProfileAsync(second, CancellationToken.None);
        await page.ProfileChangedAsync();
        page.Settings.Picker.Row(PipelineSteps.Concepts)!.Selected!.Model.ShouldBeNull();
        await session.SelectProfileAsync(first, CancellationToken.None);
        await page.ProfileChangedAsync();
        page.Settings.Picker.Row(PipelineSteps.Concepts)!.Selected!.Model!.Id.ShouldBe("claude-haiku-5-5");
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Concepts, second.Id, CancellationToken.None)).ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task TheEffortPickerStartsWithAutomaticAndShowsOnlyForModelsThatTakeAnEffort()
    {
        using var h = new DesktopHarness(s => s.AddSingleton<IGpuInfo>(new FakeGpu(null)));
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var text = h.Get<Localizer>();
        var concepts = picker.Row(PipelineSteps.Concepts)!;

        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");

        concepts.HasEfforts.ShouldBeTrue();
        concepts.Efforts[0].Value.ShouldBeEmpty();
        concepts.Efforts[0].Label.ShouldBe(text["models.effort.auto"]);
        concepts.Effort.ShouldBe(concepts.Efforts[0]);
        concepts.Effort = concepts.Efforts.First(e => e.Value == "high");
        concepts.Selected = concepts.Options.First(o => o.Model != null && o.Model.Provider == "zai" && o.Model.Efforts.Count == 0);
        concepts.HasEfforts.ShouldBeFalse();

        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        concepts.Effort = concepts.Efforts.First(e => e.Value == "high");
        await picker.SaveAsync();
        StepModelResolver.Options(await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Concepts, profile.Id, CancellationToken.None)).Effort.ShouldBe("high");
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        picker.Row(PipelineSteps.Concepts)!.Effort!.Value.ShouldBe("high");
    }

    [AvaloniaFact]
    public async Task AProviderWithoutAKeyStopsTheProcessUntilAKeyIsAdded()
    {
        var fake = new FakeChat().Concepts(FakeChat.Distinct(2));
        FakeApiKeyDialog? dialog = null;
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton<IApiKeyDialog>(p => dialog = new FakeApiKeyDialog(p.GetRequiredService<IApiKeyStore>()));
        });
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Provider == "openai" && o.Model.Capabilities.HasFlag(ModelCapability.Chat));
        page.Ideas.Count = 2;

        // No key and the dialog closed without one: nothing is sent, the reason names the provider, the choice is not stored.
        await page.Ideas.GenerateAsync();
        dialog!.Asked.ShouldBe(["openai"]);
        fake.Requests.ShouldBeEmpty();
        page.Ideas.Status.ShouldContain("openai");
        row.NoteIsWarning.ShouldBeTrue();
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Concepts, profile.Id, CancellationToken.None)).ShouldBeNull();

        // A working key is added in the dialog: the same press goes on.
        dialog.Secret = Secret;
        await page.Ideas.GenerateAsync();
        fake.Requests.Count.ShouldBe(1);
        fake.KeysUsed.ShouldBe([Secret]);
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Concepts, profile.Id, CancellationToken.None))!.Provider.ShouldBe("openai");
    }

    [AvaloniaFact]
    public async Task SettingsSavesOnlyAKeyThatPassedItsTest()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid key"}}""");
        using var h = new DesktopHarness(s => s.AddSingleton(fake.Transport));
        await FirstRunTests.CreateAsync(h);
        var keys = h.Get<KeysSettingsViewModel>();
        await keys.ActivateAsync();
        keys.Provider = keys.Providers.First(p => p.Provider.Code == "openai");
        keys.Secret = "fake-wrong-key-0123456789";

        await keys.AddAsync();

        keys.Keys.ShouldBeEmpty();
        keys.Status.ShouldStartWith("✗");
        (await h.Get<IApiKeyStore>().ListAsync("openai", CancellationToken.None)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ASavedKeyThatFailsItsTestDoesNotPassTheKeyGate()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid key"}}""");
        var chat = new FakeChat().Concepts(FakeChat.Distinct(2));
        FakeApiKeyDialog? dialog = null;
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton(fake.Transport);
            s.AddSingleton<IChatClientFactory>(chat);
            s.AddSingleton<IApiKeyDialog>(p => dialog = new FakeApiKeyDialog(p.GetRequiredService<IApiKeyStore>()));
        });
        await FirstRunTests.CreateAsync(h);

        // A key saved by 0.1.5 although its test failed (no "passed" time): the gate tests it now, and it fails.
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "openai", Label = "old", Secret = "fake-expired-key-0123456789" }, CancellationToken.None);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Provider == "openai" && o.Model.Capabilities.HasFlag(ModelCapability.Chat));
        page.Ideas.Count = 2;

        await page.Ideas.GenerateAsync();

        fake.Requests.Single().Uri.Host.ShouldBe("api.openai.com");
        dialog!.Asked.ShouldBe(["openai"]);
        chat.Requests.ShouldBeEmpty();
        page.Ideas.Status.ShouldContain("openai");
    }

    [AvaloniaFact]
    public async Task ASavedKeyThatPassesItsTestNowIsMarkedAndUsed()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """{"data":[]}""");
        using var h = new DesktopHarness(s => s.AddSingleton(fake.Transport));
        await FirstRunTests.CreateAsync(h);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "openai", Label = "old", Secret = "fake-good-key-0123456789" }, CancellationToken.None);
        var model = h.Get<IModelCatalog>().All.First(m => m.Provider == "openai" && m.Capabilities.HasFlag(ModelCapability.Chat));

        (await h.Get<StepModelResolver>().ProvidersWithoutWorkingKeyAsync([model], CancellationToken.None)).ShouldBeEmpty();
        (await h.Get<StepModelResolver>().ProvidersWithoutWorkingKeyAsync([model], CancellationToken.None)).ShouldBeEmpty();

        fake.Requests.Count.ShouldBe(1, "a key that passed is not tested again");
        (await h.Get<IApiKeyStore>().ListAsync("openai", CancellationToken.None)).Single().LastOkAtUtc.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task TheKeyDialogSavesOnlyAKeyThatPassedItsTest()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid key"}}""").Answer(HttpStatusCode.OK, """{"data":[]}""");
        using var h = new DesktopHarness(s => s.AddSingleton(fake.Transport));
        await FirstRunTests.CreateAsync(h);
        var prompt = ((ApiKeyDialog)h.Get<IApiKeyDialog>()).Prompt("anthropic")!;
        var closed = false;
        prompt.CloseRequested += (_, _) => closed = true;
        prompt.Secret = "fake-wrong-key-0123456789";

        await prompt.TestAndSaveAsync();

        prompt.Saved.ShouldBeFalse();
        prompt.Failed.ShouldBeTrue();
        closed.ShouldBeFalse();
        (await h.Get<IApiKeyStore>().ListAsync("anthropic", CancellationToken.None)).ShouldBeEmpty();

        prompt.Secret = Secret;
        await prompt.TestAndSaveAsync();

        prompt.Saved.ShouldBeTrue();
        closed.ShouldBeTrue();
        prompt.Secret.ShouldBeEmpty();
        (await h.Get<IApiKeyStore>().ListAsync("anthropic", CancellationToken.None)).Single().LastOkAtUtc.ShouldNotBeNull();
        fake.Requests.Count.ShouldBe(2);
    }
}
