using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Providers;

namespace TMConcept.Tests.Core;

/// <summary>The one retry rule for JSON answers: the same request again at most twice, then a clear "parse" error.</summary>
public sealed class JsonAnswerRetryTests
{
    [Fact]
    public async Task AReadableFirstAnswerIsNotAskedAgain()
    {
        var calls = 0;
        var result = await JsonAnswerRetry.RunAsync("Test", (_, _) => { calls++; return Task.FromResult<string?>("ok"); }, NullLogger.Instance, CancellationToken.None);

        result.ShouldBe("ok");
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task AnUnreadableAnswerIsAskedAgainUpToTwiceMore()
    {
        var numbers = new List<int>();
        var result = await JsonAnswerRetry.RunAsync("Test", (n, _) =>
        {
            numbers.Add(n);
            return Task.FromResult(n < 3 ? null : "third");
        }, NullLogger.Instance, CancellationToken.None);

        result.ShouldBe("third");
        numbers.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task ThreeUnreadableAnswersEndWithAParseError()
    {
        var calls = 0;
        var error = await Should.ThrowAsync<ProviderException>(() =>
            JsonAnswerRetry.RunAsync<string>("Test", (_, _) => { calls++; return Task.FromResult<string?>(null); }, NullLogger.Instance, CancellationToken.None));

        error.Code.ShouldBe("parse");
        calls.ShouldBe(JsonAnswerRetry.Attempts);
        JsonAnswerRetry.Attempts.ShouldBe(3);
    }

    [Fact]
    public async Task AProviderErrorIsNotRetried()
    {
        var calls = 0;
        await Should.ThrowAsync<ProviderException>(() => JsonAnswerRetry.RunAsync<string>("Test", (_, _) =>
        {
            calls++;
            throw new ProviderException("no") { Code = "auth" };
        }, NullLogger.Instance, CancellationToken.None));

        calls.ShouldBe(1);
    }
}