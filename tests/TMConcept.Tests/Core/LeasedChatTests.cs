using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Core;

/// <summary>Every chat call holds a graphics card lease: a local prompt model is on the card while it answers and leaves it for the image model.</summary>
public sealed class LeasedChatTests
{
    [Fact]
    public async Task ALocalChatModelHoldsTheCardOnlyWhileItAnswers()
    {
        var llm = new FakeLocalModel("qwen3");
        var image = new FakeLocalModel("z-image");
        var gpu = new GpuCoordinator(new ModelDirectory([new Source(llm), new Source(image)]),
            new FakeVramProbe(new VramReading(16L << 30, 14L << 30, true, "test")), TestWaiters.Fast(), TimeProvider.System, NullLogger<GpuCoordinator>.Instance);
        ILocalModel? onCardWhileAnswering = null;
        var fake = new FakeChat().Answer(_ =>
        {
            onCardWhileAnswering = gpu.Loaded;
            return new ChatResponse("{\"prompts\": [\"a\"]}", new ChatUsage(10, 0, 0, 10), null, "end_turn", "r");
        });
        var chats = new LeasedChatClientFactory(fake, gpu);

        await chats.Create(llm.Catalog, string.Empty).SendAsync(new ChatRequest("s", "u"), CancellationToken.None);
        onCardWhileAnswering.ShouldBe(llm);

        await using (await gpu.AcquireAsync(image.Catalog, CancellationToken.None))
        {
            llm.Unloads.ShouldBe(1, "the prompt model left the card before the image model");
        }

        llm.Events.ShouldBe(["load qwen3", "unload qwen3"]);
    }

    private sealed class Source(ILocalModel model) : ILocalModelSource
    {
        public ILocalModel? Create(CatalogModel catalog) => catalog.Id == model.Id ? model : null;
    }
}