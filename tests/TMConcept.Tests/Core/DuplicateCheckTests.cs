using Shouldly;
using TMConcept.Core.Duplicates;

namespace TMConcept.Tests.Core;

public sealed class DuplicateCheckTests
{
    private static DuplicateSubject S(int number, string title, string summary = "") => new(Guid.NewGuid(), number, title, summary);

    [Fact]
    public void SameTitleIsADuplicateEvenWithOtherCaseAndLetters()
    {
        var verdicts = DuplicateCheck.Sift([S(0, "BİZANS'IN SURLARI")], [S(1, "Bizansın surları")]);
        verdicts.Single().Result.Kind.ShouldBe(DuplicateKind.Duplicate);
        verdicts.Single().Result.OtherNumber.ShouldBe(1);
    }

    [Fact]
    public void DifferentPartOfASeriesIsAContinuation()
    {
        var result = DuplicateCheck.Sift([S(0, "Nika Riot — Part 3")], [S(1, "Nika Riot — Part 2")]).Single().Result;
        result.Kind.ShouldBe(DuplicateKind.Continuation);
        result.Accepted.ShouldBeTrue();
    }

    [Fact]
    public void SamePartIsADuplicate() =>
        DuplicateCheck.Sift([S(0, "Nika İsyanı 2. bölüm")], [S(1, "Nika İsyanı — Bölüm 2")]).Single().Result.Kind.ShouldBe(DuplicateKind.Duplicate);

    [Fact]
    public void UnrelatedTitleIsNew() =>
        DuplicateCheck.Sift([S(0, "Roman concrete under the sea")], [S(1, "Greek fire formula")]).Single().Result.Kind.ShouldBe(DuplicateKind.New);

    [Fact]
    public void SecondCopyInTheSameBatchIsDropped()
    {
        var verdicts = DuplicateCheck.Sift([S(0, "The water clock of Ctesibius"), S(0, "The Water Clock of Ctesibius!")], []);
        verdicts[0].Result.Accepted.ShouldBeTrue();
        verdicts[1].Result.Kind.ShouldBe(DuplicateKind.Duplicate);
    }

    [Fact]
    public void SplitPartReadsCommonMarkers()
    {
        DuplicateCheck.SplitPart("Punic Wars – Part 4").ShouldBe(("Punic Wars", 4));
        DuplicateCheck.SplitPart("Lale Devri (5. kısım)").ShouldBe(("Lale Devri", 5));
        DuplicateCheck.SplitPart("No marker").ShouldBe(("No marker", 0));
    }

    [Fact]
    public void CodeIsLanguageNeutral()
    {
        var result = new DuplicateResult(DuplicateKind.Similar, 0.714, Guid.Empty, 212, "x");
        result.Code.ShouldBe("similar:212:0.71");
    }
}
