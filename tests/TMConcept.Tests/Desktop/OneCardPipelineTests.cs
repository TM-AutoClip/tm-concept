using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Speech;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The 0.1.17 packages together in the real composition root (fake servers and a fake NVIDIA card only, nothing runs on a real card): a local
/// Ollama model writes the image prompts, leaves the card before the local image model takes it, the image model leaves it before a Python
/// voice on the card speaks, and Ahenk Cep (processor only) never takes the card. One local model on the card at a time.
/// </summary>
public sealed class OneCardPipelineTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [AvaloniaFact]
    public async Task OllamaThenImagesThenAPythonVoiceTakeTheCardOneAtATimeAndAhenkCepNeverDoes()
    {
        using var ollama = new FakeOllamaServer();
        ollama.Chat(200, FakeOllamaServer.Answer("{\"prompts\":[\"a castle at dawn\"]}"));
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton(FakeLocalServer.Transport());
            s.AddSingleton<IGpuInfo>(new FakeGpu(16L << 30));
        });
        await FirstRunTests.CreateAsync(h);
        var endpoint = new LocalEndpoint
        {
            Name = "Ollama",
            BaseUrl = ollama.Root,
            Kind = EndpointKind.Ollama,
            Models = [new EndpointModel { Id = "qwen3:8b", ContextTokens = 16384, MaxOutputTokens = 8192, JsonSchema = true }],
        };
        await h.Get<EndpointRegistry>().SaveAsync(endpoint, None);

        var catalog = h.Get<IModelCatalog>();
        var directory = h.Get<IModelDirectory>();
        var gpu = h.Get<IGpuCoordinator>();
        var language = catalog.With(ModelCapability.Chat).Single(m => m.Id == "qwen3:8b");
        var image = catalog.Find(ModelProviders.Local, "flux2-klein-4b").ShouldNotBeNull();
        var voice = catalog.Find(ModelProviders.Local, SpeechEngines.VoxCpm2).ShouldNotBeNull();
        var ahenkCep = catalog.Find(ModelProviders.Local, SpeechEngines.AhenkCep).ShouldNotBeNull();

        // Every kind of local model comes from its own source in the real container.
        directory.Get(language).ShouldBeOfType<EndpointLocalModel>();
        directory.Get(image).ShouldBeOfType<TMConcept.Engines.Images.SdCppLocalModel>();
        directory.Get(voice).ShouldBeOfType<TMConcept.Speech.Python.PythonLocalModel>();
        ((ILocalModel)directory.Get(ahenkCep)).Device.ShouldBe(ModelDevice.Cpu);

        // 1. The local language model writes the image prompts (the chat goes through its lease).
        var chat = h.Get<IChatClientFactory>().Create(language, string.Empty);
        await chat.SendAsync(new ChatRequest("Write image prompts.", "A castle."), None);
        ollama.Loaded.ShouldBe(["qwen3:8b"]);
        gpu.Loaded.ShouldBe(directory.Get(language));

        // 2. The image model takes the card: Ollama is asked to unload first.
        await using (var lease = await gpu.AcquireAsync(image, None))
        {
            lease.OnCard.ShouldBeTrue();
            ollama.Loaded.ShouldBeEmpty("the language model left the card before the image model took it");
            gpu.Loaded.ShouldBe(directory.Get(image));
        }

        // 3. A Python voice on the NVIDIA card takes it next; Ahenk Cep speaks on the processor and leaves the card alone.
        await using (var lease = await gpu.AcquireAsync(voice, None))
        {
            lease.OnCard.ShouldBeTrue();
            gpu.Loaded.ShouldBe(directory.Get(voice));
        }

        await using (var lease = await gpu.AcquireAsync(ahenkCep, None))
        {
            lease.OnCard.ShouldBeFalse();
            gpu.Loaded.ShouldBe(directory.Get(voice), "a processor model does not unload the model on the card");
        }

        // The end of the work frees the card.
        await gpu.FreeAsync(None);
        gpu.Loaded.ShouldBeNull();
    }
}
