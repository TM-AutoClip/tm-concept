using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>Graphics card memory classes: a card's class from what its driver reports, a model's class, and "a card sees its own class and lower".</summary>
public sealed class VramTierTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(16.0, VramTier.Gb16)]
    [InlineData(15.8, VramTier.Gb16)]
    [InlineData(15.98, VramTier.Gb16)]
    [InlineData(14.8, VramTier.Gb16)]
    [InlineData(14.0, VramTier.Gb12)]
    [InlineData(13.9, VramTier.Gb12)]
    [InlineData(12.0, VramTier.Gb12)]
    [InlineData(11.5, VramTier.Gb12)]
    [InlineData(11.0, VramTier.Gb8)]
    [InlineData(10.0, VramTier.Gb8)]
    [InlineData(8.0, VramTier.Gb8)]
    [InlineData(7.9, VramTier.Gb8)]
    [InlineData(6.0, VramTier.Gb6)]
    [InlineData(4.0, VramTier.Gb4)]
    [InlineData(3.7, VramTier.Gb4)]
    [InlineData(3.6, VramTier.None)]
    [InlineData(3.0, VramTier.None)]
    [InlineData(20.0, VramTier.Gb16)]
    [InlineData(23.9, VramTier.Gb24)]
    [InlineData(32.0, VramTier.Gb32)]
    [InlineData(48.0, VramTier.Gb32)]
    public void ACardsClassToleratesWhatTheDriverReports(double gibibytes, VramTier expected) =>
        VramTiers.ForCard((long)(gibibytes * GiB)).ShouldBe(expected);

    [Fact]
    public void UnknownMemoryHasNoClass()
    {
        VramTiers.ForCard(null).ShouldBe(VramTier.None);
        VramTiers.ForCard(0).ShouldBe(VramTier.None);
    }

    [Theory]
    [InlineData(0, VramTier.None)]
    [InlineData(4, VramTier.Gb4)]
    [InlineData(6, VramTier.Gb6)]
    [InlineData(7, VramTier.Gb8)]
    [InlineData(16, VramTier.Gb16)]
    [InlineData(17, VramTier.Gb24)]
    [InlineData(40, VramTier.Gb32)]
    public void AModelsClassIsTheSmallestThatHoldsIt(double gigabytes, VramTier expected) => VramTiers.ForModel(gigabytes).ShouldBe(expected);

    [Fact]
    public void ACardSeesItsOwnClassAndEveryLowerOne()
    {
        VramTiers.Visible(VramTier.Gb16).ShouldBe([VramTier.Gb4, VramTier.Gb6, VramTier.Gb8, VramTier.Gb12, VramTier.Gb16]);
        VramTiers.Sees(VramTier.Gb16, VramTier.Gb24).ShouldBeFalse("a higher class is red: not usable");
        VramTiers.Sees(VramTier.Gb16, VramTier.Gb16).ShouldBeTrue();
        VramTiers.Sees(VramTier.None, VramTier.Gb4).ShouldBeFalse("no card sees no card class");
        VramTiers.Sees(VramTier.None, VramTier.None).ShouldBeTrue("a processor model runs everywhere");
        VramTiers.Visible(VramTier.None).ShouldBeEmpty();

        // The 0.1.16 tester's card: an AMD "16 GB" card that reports 15.8 GB runs the 16 GB class (Z-Image full).
        VramTiers.Sees(VramTiers.ForCard((long)(15.8 * GiB)), VramTiers.ForModel(16)).ShouldBeTrue();
    }

    [Fact]
    public void WithoutAMeasurementAnEighthAndHalfAGibibyteAreKeptBack()
    {
        VramReading.Estimate(16 * GiB).ShouldBe((14 * GiB) - (GiB / 2));
        VramReading.Estimate(0).ShouldBe(0);
        VramReading.Estimate(GiB / 4).ShouldBe(0, "never below zero");
        new VramReading((long)(15.8 * GiB), GiB, true, "PDH").Tier.ShouldBe(VramTier.Gb16);
    }

    [Fact]
    public void AModelNeedNamesItsClassAndTheBestFittingQuality()
    {
        ModelMemoryNeed.FromGigabytes(ModelQualities.Standard, 6, 0).Tier.ShouldBe(VramTier.Gb6);
        ModelMemoryNeed.FromGigabytes(ModelQualities.Standard, 0, 4).Tier.ShouldBe(VramTier.None);
        ModelMemoryNeed[] needs =
        [
            ModelMemoryNeed.FromGigabytes(ModelQualities.Low, 4, 0),
            ModelMemoryNeed.FromGigabytes(ModelQualities.Standard, 8, 0),
            ModelMemoryNeed.FromGigabytes(ModelQualities.High, 14, 0),
        ];
        ModelMemoryNeed.Best(needs, 10 * GiB)!.Quality.ShouldBe(ModelQualities.Standard);
        ModelMemoryNeed.Best(needs, 20 * GiB)!.Quality.ShouldBe(ModelQualities.High);
        ModelMemoryNeed.Best(needs, 3 * GiB).ShouldBeNull("nothing fits: weights go to main memory or the processor");
    }
}