using Shouldly;
using TMConcept.Core.Security;

namespace TMConcept.Tests.Core;

public sealed class RedactorTests
{
    [Theory]
    [InlineData("x-api-key: abcdefghijklmnop")]
    [InlineData("Authorization: Bearer abcdefghijklmnop")]
    [InlineData("{\"api_key\":\"abcdefghijklmnop\"}")]
    [InlineData("https://x.example/?key=abcdefghijklmnop")]
    public void SecretsAreMasked(string text) => Redactor.Redact(text).ShouldNotContain("abcdefghijklmnop");

    [Fact]
    public void KeyLikeTextIsMasked() => Redactor.Redact("used sk-ant-" + new string('Z', 24)).ShouldNotContain(new string('Z', 24));

    [Fact]
    public void KnownValuesAreMasked() => Redactor.Redact("echo: my-secret-value-1", ["my-secret-value-1"]).ShouldBe("echo: ***");

    [Fact]
    public void AShortUserChosenTokenIsMaskedAsAWholeWord()
    {
        // llama-server --api-key secret: the patterns cannot know the word, the value can (0.1.16 skipped values under 8 characters).
        Redactor.Redact("401: invalid api key 'secret' (secret)", ["secret"]).ShouldBe("401: invalid api key '***' (***)");
        Redactor.Redact("the secretary said no", ["secret"]).ShouldBe("the secretary said no", "a short value never cuts letters out of a word");
        Redactor.Redact("key=a1", ["a1"]).ShouldNotContain("a1");
    }

    [Fact]
    public void MaskShowsOnlyTheEnds() => Redactor.Mask("abcd1234567890wxyz").ShouldBe("abcd…wxyz");
}
