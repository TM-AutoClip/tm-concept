using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Step choices stay exactly as the user last chose them (0.1.17): saved at every change, never reset by a page switch, a new key or a
/// missing server; a saved model that is not offered now is shown as unavailable and kept.
/// </summary>
public sealed class ModelChoiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<Profile> ProfileAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        return profile;
    }

    private static async Task Until(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 150 && !await condition(); i++)
        {
            await Task.Delay(20);
        }

        (await condition()).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task AChoiceIsSavedTheMomentItIsMadeAndAKeyChangesNothing()
    {
        using var h = new DesktopHarness();
        var profile = await ProfileAsync(h);
        var store = h.Get<IStepModelStore>();
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var row = picker.Row(PipelineSteps.Concepts)!;

        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        await Until(async () => (await store.GetAsync(PipelineSteps.Concepts, profile.Id, None))?.Model == "claude-sonnet-5-5");
        row.Effort = row.Efforts.First(e => e.Value == "medium");
        await Until(async () => StepModelResolver.Options(await store.GetAsync(PipelineSteps.Concepts, profile.Id, None)).Effort == "medium");

        // A key added meanwhile (no process was started, nothing was prepared): the next page shows the same choice.
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = "fake-test-key-0123456789abcdef" }, None);
        var again = h.Get<StepPickerViewModel>();
        await again.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var shown = again.Row(PipelineSteps.Concepts)!;
        shown.Selected!.Model!.Id.ShouldBe("claude-sonnet-5-5");
        shown.Effort!.Value.ShouldBe("medium");
    }

    [AvaloniaFact]
    public async Task ASavedModelThatIsNotOfferedNowIsKeptAndShownAsUnavailable()
    {
        using var h = new DesktopHarness();
        var profile = await ProfileAsync(h);
        var store = h.Get<IStepModelStore>();
        var endpoint = new LocalEndpoint { Name = "Ollama", BaseUrl = "http://localhost:11434/v1", Models = [new EndpointModel { Id = "qwen3:8b" }] };
        var saved = new StepModelChoice { Step = PipelineSteps.Topics, ProfileId = profile.Id, Provider = EndpointCodes.For(endpoint.Id), Model = "qwen3:8b" };
        await store.SaveAsync(saved, None);

        // The server is not saved (removed, or not loaded yet): the choice is shown as unavailable, with the reason, and kept.
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var row = picker.Row(PipelineSteps.Topics)!;
        var text = h.Get<Localizer>();
        row.Selected!.Kind.ShouldBe(StepOptionKind.Unavailable);
        row.Selected.Label.ShouldBe(text.Format("models.unavailable.label", "qwen3:8b"));
        await Task.Delay(50);
        row.Note.ShouldBe(text["models.unavailable.server"]);
        row.NoteIsError.ShouldBeTrue();
        picker.Choice(PipelineSteps.Topics)!.Provider.ShouldBe(saved.Provider);

        (await picker.PrepareAsync([PipelineSteps.Topics], "install.purpose.topics", None)).ShouldBeFalse();
        picker.Status.ShouldBe(text.Format("picker.not_started.unavailable", row.Title));
        (await store.GetAsync(PipelineSteps.Topics, profile.Id, None))!.Provider.ShouldBe(saved.Provider, "never replaced on its own");

        // The server comes back: the same model is chosen again, nothing to redo.
        await h.Get<EndpointRegistry>().SaveAsync(endpoint, None);
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var back = picker.Row(PipelineSteps.Topics)!;
        back.Selected!.Kind.ShouldBe(StepOptionKind.Model);
        back.Selected.Model!.Provider.ShouldBe(saved.Provider);
        back.Options.ShouldNotContain(o => o.Kind == StepOptionKind.Unavailable);

        // The user picks another model: that one is saved and the unavailable entry is gone.
        await h.Get<EndpointRegistry>().DeleteAsync(endpoint.Id, None);
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var again = picker.Row(PipelineSteps.Topics)!;
        again.Selected = again.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        await Until(async () => (await store.GetAsync(PipelineSteps.Topics, profile.Id, None))?.Model == "claude-haiku-5-5");
        again.Options.ShouldNotContain(o => o.Kind == StepOptionKind.Unavailable);
    }

    [AvaloniaFact]
    public async Task ARequiredStepWithAnUnavailableModelSaysSoInsteadOfAskingForAModel()
    {
        using var h = new DesktopHarness();
        var profile = await ProfileAsync(h);
        var saved = new StepModelChoice { Step = PipelineSteps.Concepts, ProfileId = profile.Id, Provider = "anthropic", Model = "claude-retired-4" };
        await h.Get<IStepModelStore>().SaveAsync(saved, None);
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var text = h.Get<Localizer>();
        var row = picker.Row(PipelineSteps.Concepts)!;

        (await picker.PrepareAsync([PipelineSteps.Concepts], "install.purpose.concepts", None)).ShouldBeFalse();

        picker.Status.ShouldBe(text.Format("picker.not_started.unavailable", row.Title));
        row.Note.ShouldBe(text["models.unavailable.catalog"]);
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Concepts, profile.Id, None))!.Model.ShouldBe("claude-retired-4");
    }

    [AvaloniaFact]
    public async Task APickerListBeingReplacedSavesNothing()
    {
        using var h = new DesktopHarness();
        var profile = await ProfileAsync(h);
        var store = h.Get<IStepModelStore>();
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var row = picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        row.Effort = row.Efforts.First(e => e.Value == "high");
        await Until(async () => StepModelResolver.Options(await store.GetAsync(PipelineSteps.Concepts, profile.Id, None)).Effort == "high");

        // What a ComboBox does while its items are replaced: null for a moment.
        row.Selected = null;
        row.Effort = null;
        await Task.Delay(100);

        var kept = await store.GetAsync(PipelineSteps.Concepts, profile.Id, None);
        kept!.Model.ShouldBe("claude-sonnet-5-5");
        StepModelResolver.Options(kept).Effort.ShouldBe("high");
    }
}