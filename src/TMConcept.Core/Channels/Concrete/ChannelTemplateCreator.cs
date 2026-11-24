using Microsoft.Extensions.Logging;
using TMConcept.Core.References;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Channels;

/// <summary>
/// The template draft of "Kanaldan esinlen" (0.1.53, rule 4: a template itself never changes): always a NEW template ("&lt;channel&gt; esinli")
/// with the merged tempo profile of the channel's samples as its reference video (K18 fields: the tempo set to the nearest preset, the plan's
/// targets from the numbers) and the defaults of a new template (drawn by the app, with its finishing layer). It is added only while there are
/// fewer than <see cref="VideoStyleLimits.Max"/> templates (rule 20); an existing template is never touched, also when an id would clash.
/// </summary>
public sealed class ChannelTemplateCreator(IVideoStyleStore store, TimeProvider clock, ILogger<ChannelTemplateCreator> logger) : IChannelTemplateCreator
{
    /// <summary>The draft of a template named <paramref name="name"/> (cut to <see cref="VideoStyleLimits.NameMax"/>) with <paramref name="tempo"/>.</summary>
    public static VideoStyle Draft(string name, ReferenceTempoProfile tempo, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tempo);
        var trimmed = (name ?? string.Empty).Trim();
        return new VideoStyle
        {
            Name = trimmed.Length <= VideoStyleLimits.NameMax ? trimmed : trimmed[..VideoStyleLimits.NameMax].TrimEnd(),
            Drawer = SceneDrawer.App,
            Finish = true,
            Reference = tempo,
            Tempo = ReferenceTempoMapper.Suggest(tempo).Tempo,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    /// <inheritdoc />
    public async Task<bool> HasRoomAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Count < VideoStyleLimits.Max;

    /// <inheritdoc />
    public async Task<ChannelTemplateOutcome> CreateAsync(VideoStyle draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var existing = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count >= VideoStyleLimits.Max)
        {
            logger.LogInformation("The channel's template draft was not added: the limit of {Max} templates is reached", VideoStyleLimits.Max);
            return ChannelTemplateOutcome.LimitReached;
        }

        var style = VideoStyleRules.Copy(draft);
        if (existing.Any(s => s.Id == style.Id))
        {
            style.Id = Guid.NewGuid();
        }

        style.CreatedAtUtc = style.UpdatedAtUtc = clock.GetUtcNow();
        try
        {
            await store.SaveAsync(style, cancellationToken).ConfigureAwait(false);
        }
        catch (VideoStyleLimitException ex)
        {
            logger.LogInformation(ex, "The channel's template draft was not added: the limit was reached meanwhile");
            return ChannelTemplateOutcome.LimitReached;
        }

        draft.Id = style.Id;
        logger.LogInformation("The channel's template draft was added as a new template ({Tempo})", style.Tempo);
        return ChannelTemplateOutcome.Created;
    }
}
