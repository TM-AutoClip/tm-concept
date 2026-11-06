namespace TMConcept.Providers.Pricing;

/// <summary>
/// What a downloaded price may be. A price outside the range, or more than <see cref="MaxChange"/> times the built-in one (up or down), is
/// a broken file or a typo: that entry is dropped and the built-in price stays.
/// </summary>
public static class PriceLimits
{
    /// <summary>Highest token price (USD per one million tokens).</summary>
    public const decimal MaxTokenUsd = 1000m;

    /// <summary>Highest price of one image (USD).</summary>
    public const decimal MaxImageUsd = 10m;

    /// <summary>Highest speech price (USD per one million characters).</summary>
    public const decimal MaxSpeechUsd = 1000m;

    /// <summary>Largest change against the built-in price (a factor, both ways).</summary>
    public const decimal MaxChange = 20m;

    /// <summary>How far in the future a price date may be (days; clocks differ).</summary>
    public const int FutureDays = 2;
}
