using System.Net;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

public sealed class KeysAndModelsTests
{
    [AvaloniaFact]
    public async Task AKeyIsSavedTestedAndShownMasked()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """{"data":[]}""");
        using var h = new DesktopHarness(s => s.AddSingleton(fake.Transport));
        await FirstRunTests.CreateAsync(h);
        var keys = h.Get<KeysSettingsViewModel>();
        await keys.ActivateAsync();
        keys.Provider = keys.Providers.First(p => p.Provider.Code == "anthropic");
        keys.Secret = "fake-test-key-0123456789abcdef";

        await keys.AddAsync();

        var row = keys.Keys.Single();
        row.Masked.ShouldNotContain("0123456789abcdef");
        row.Failed.ShouldBeFalse();
        fake.Requests.Single().Uri.Host.ShouldBe("api.anthropic.com");
        (await h.Get<IApiKeyStore>().ListAsync("anthropic", CancellationToken.None)).Single().LastOkAtUtc.ShouldNotBeNull();
        keys.Secret.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task NoStepHasAModelUntilTheUserChoosesOne()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var models = h.Get<ModelsSettingsViewModel>();

        await models.ActivateAsync();

        models.Rows.Count.ShouldBe(PipelineSteps.All.Count);
        models.Rows.ShouldAllBe(r => r.Selected!.Model == null);
        await models.SaveAsync();
        (await h.Get<IStepModelStore>().ListAsync(CancellationToken.None)).ShouldBeEmpty();

        var concepts = models.Rows.First(r => r.Step.Code == "concepts");
        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        concepts.Effort = "high";
        await models.SaveAsync();
        var saved = (await h.Get<IStepModelStore>().ListAsync(CancellationToken.None)).Single();
        saved.Model.ShouldBe("claude-sonnet-5-5");
        StepModelResolver.Options(saved).Effort.ShouldBe("high");
    }
}
