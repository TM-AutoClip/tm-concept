namespace TMConcept.Core.Channels;

/// <summary>What became of the template draft of "Kanaldan esinlen" (<see cref="ChannelTemplateCreator.CreateAsync"/>).</summary>
public enum ChannelTemplateOutcome
{
    /// <summary>A new template was added.</summary>
    Created = 0,

    /// <summary>Not added: there are already <see cref="Styles.VideoStyleLimits.Max"/> templates (rule 20).</summary>
    LimitReached = 1,
}
