using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Tests.Engines;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.55: the one way a piece is made (the music page and a video's music step had a copy each): every failure of a model comes out named as
/// a <see cref="MusicGenerationException"/> (too little memory as <see cref="MusicFailure.Memory"/>), a cancellation stays a cancellation.
/// No model runs.
/// </summary>
public sealed class MusicPieceRunnerTests
{
    private static readonly CatalogModel Online = new() { Provider = "elevenlabs", Id = "music_v1", Name = "Eleven Music", Capabilities = ModelCapability.Music };

    private static MusicGenerationRequest Request() =>
        new(new ResolvedModel(Online, new ApiKey { Provider = Online.Provider }, string.Empty), "calm", 30, null, true, "out", 1);

    [Fact]
    public async Task AStrayFailureIsNamedAndAMemoryShortfallKeepsItsKind()
    {
        var runner = new MusicPieceRunner(new ImageBackendTests.FakeCoordinator(), NullLogger<MusicPieceRunner>.Instance);

        var stray = await Should.ThrowAsync<MusicGenerationException>(() => runner.RunAsync(new Throwing(new InvalidOperationException("no process")), Request(),
            CancellationToken.None));
        var memory = await Should.ThrowAsync<MusicGenerationException>(() => runner.RunAsync(new Throwing(new MemoryShortfallException("too little")), Request(),
            CancellationToken.None));
        var own = await Should.ThrowAsync<MusicGenerationException>(() => runner.RunAsync(
            new Throwing(new MusicGenerationException("empty") { Code = MusicFailure.Empty }), Request(), CancellationToken.None));

        stray.Code.ShouldBe(MusicFailure.Music);
        stray.Message.ShouldBe("no process");
        stray.InnerException.ShouldBeOfType<InvalidOperationException>();
        memory.Code.ShouldBe(MusicFailure.Memory);
        own.Code.ShouldBe(MusicFailure.Empty, "the model's own failure passes as it is");
        await Should.ThrowAsync<OperationCanceledException>(() => runner.RunAsync(new Throwing(new OperationCanceledException()), Request(), CancellationToken.None));
    }

    [Fact]
    public async Task APieceStartsWithNothingCountedThenTheModelsOwnProgress()
    {
        var runner = new MusicPieceRunner(new ImageBackendTests.FakeCoordinator(), NullLogger<MusicPieceRunner>.Instance);
        var seen = new List<MusicProgress>();
        var request = Request() with { Progress = new TMConcept.Core.Chat.ForwardProgress<MusicProgress>(seen.Add) };

        await Should.ThrowAsync<MusicGenerationException>(() => runner.RunAsync(new Throwing(new MusicGenerationException("empty")), request,
            CancellationToken.None));

        seen.ShouldHaveSingleItem().ShouldBe(MusicProgress.Unknown, "an online model counts nothing: an indeterminate bar with the time so far");
    }

    private sealed class Throwing(Exception error) : IMusicGenerator
    {
        public bool Handles(CatalogModel model) => true;

        public bool Ready(CatalogModel model) => true;

        public Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken) => Task.FromException<GeneratedMusic>(error);
    }
}
