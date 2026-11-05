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
    public void MaskShowsOnlyTheEnds() => Redactor.Mask("abcd1234567890wxyz").ShouldBe("abcd…wxyz");
}
