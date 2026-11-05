using Shouldly;
using TMConcept.Core.Text;

namespace TMConcept.Tests.Core;

public sealed class TurkishFoldTests
{
    [Theory]
    [InlineData("İSTANBUL'un Çağları", "istanbul un caglari")]
    [InlineData("  Işık   ve  gölge! ", "isik ve golge")]
    [InlineData(null, "")]
    public void Folds(string? input, string expected) => TurkishFold.Fold(input).ShouldBe(expected);

    [Fact]
    public void CleanRemovesControlCharacters() => TextClean.Clean("a\0b\r\nc\u0007", 100).ShouldBe("ab\nc");
}
