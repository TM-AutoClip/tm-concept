using TMConcept.Core.Styles;

namespace TMConcept.Core.Channels;

/// <summary>
/// Adds the template draft of "Kanaldan esinlen" (0.1.53, rule 4: a template itself never changes) as a NEW template, only while there is room
/// (rule 20: at most <see cref="VideoStyleLimits.Max"/>); an existing template is never touched.
/// </summary>
public interface IChannelTemplateCreator
{
    /// <summary>Whether one more template fits (fewer than <see cref="VideoStyleLimits.Max"/>).</summary>
    Task<bool> HasRoomAsync(CancellationToken cancellationToken);

    /// <summary>Adds <paramref name="draft"/> as a new template; <see cref="ChannelTemplateOutcome.LimitReached"/> (nothing written) at the limit.</summary>
    Task<ChannelTemplateOutcome> CreateAsync(VideoStyle draft, CancellationToken cancellationToken);
}
