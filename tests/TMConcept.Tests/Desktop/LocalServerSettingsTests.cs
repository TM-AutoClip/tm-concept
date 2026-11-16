using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Ollama and LM Studio in Settings → API keys (fake servers only): found on this computer when the page opens, added with one press (their
/// models listed with context, thinking and size, the note on how the app runs them), saved with their kind, offered in the text steps, and
/// their test says what is in memory and how much of it sits on the graphics card.
/// </summary>
public sealed class LocalServerSettingsTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [AvaloniaFact]
    public async Task OllamaFoundOnThisComputerIsAddedWithOnePressAndItsTestShowsTheMemory()
    {
        using var ollama = new FakeOllamaServer { CardShare = 0.6 };
        using var h = new DesktopHarness(s => s.AddSingleton(FakeLocalServer.Transport(ollama.Root)));
        await FirstRunTests.CreateAsync(h);
        var keys = h.Get<KeysSettingsViewModel>();
        var text = h.Get<Localizer>();

        await keys.ActivateAsync();

        var editor = keys.Endpoint;
        editor.HasDetected.ShouldBeTrue();
        var found = editor.Detected.Single();
        found.Label.ShouldBe(text.Format("keys.endpoint.detected_row", "Ollama 0.40.16", ollama.Root, 3));

        await keys.UseDetectedAsync(found);

        keys.IsEndpoint.ShouldBeTrue();
        (editor.Name, editor.Address, editor.Kind).ShouldBe(("Ollama", ollama.Root, EndpointKind.Ollama));
        editor.KindNote.ShouldBe(text["keys.endpoint.note.ollama"]);
        editor.Status.ShouldBe("✓ " + text.Format("keys.endpoint.found_server", "Ollama 0.40.16", 3));
        var qwen = editor.Models.Single(m => m.Id == "qwen3:8b");
        (qwen.ContextTokens, qwen.CanThink, qwen.Think).ShouldBe(((decimal?)EndpointDefaults.ServerContextTokens, true, false));
        qwen.Label.ShouldContain("GB");
        qwen.Picked = true;
        await editor.SaveAsync();

        editor.Failed.ShouldBeFalse(editor.Status);
        var saved = h.Get<EndpointRegistry>().All.Single();
        (saved.Kind, saved.BaseUrl, saved.Models.Single().CanThink).ShouldBe((EndpointKind.Ollama, ollama.Root, true));
        editor.HasDetected.ShouldBeFalse("a saved server is not offered again");
        var catalog = h.Get<IModelCatalog>().With(ModelCapability.Chat).Single(m => m.Id == "qwen3:8b");
        catalog.Capabilities.HasFlag(ModelCapability.Schema).ShouldBeTrue();

        // The test lists the model in memory with its share on the card.
        ollama.Load("qwen3:8b", 16384);
        var row = editor.Saved.Single();
        row.Address.ShouldBe("Ollama · " + ollama.Root);
        await editor.TestAsync(row);
        row.Failed.ShouldBeFalse();
        row.Result.ShouldContain(text["keys.endpoint.loaded"]);
        row.Result.ShouldContain("qwen3:8b");

        // Opened again, nothing new is found.
        await keys.ActivateAsync();
        editor.Detected.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task LmStudioTypedWithV1IsKeptByItsOwnAddressAndAChangedAddressMakesItPlainAgain()
    {
        using var lmStudio = new FakeLmStudioServer();
        using var h = new DesktopHarness(s => s.AddSingleton(FakeLocalServer.Transport()));
        await FirstRunTests.CreateAsync(h);
        var keys = h.Get<KeysSettingsViewModel>();
        var text = h.Get<Localizer>();
        await keys.ActivateAsync();
        keys.Endpoint.HasDetected.ShouldBeFalse();
        keys.Provider = keys.Providers.Single(p => p.IsEndpoint);
        var editor = keys.Endpoint;

        editor.Address = lmStudio.Root + "/v1";
        await editor.ConnectAsync();

        (editor.Kind, editor.Address, editor.Name).ShouldBe((EndpointKind.LmStudio, lmStudio.Root, "LM Studio"));
        editor.KindNote.ShouldBe(text["keys.endpoint.note.lmstudio"]);
        editor.Models.Select(m => m.Label).ShouldContain(l => l.StartsWith("qwen/qwen3-8b — Qwen3 8B", StringComparison.Ordinal));

        editor.Address = "http://localhost:9999/v1";
        editor.Kind.ShouldBe(EndpointKind.Generic);
        editor.HasKindNote.ShouldBeFalse();
    }
}
