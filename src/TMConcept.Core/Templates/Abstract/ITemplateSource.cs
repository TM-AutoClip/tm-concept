namespace TMConcept.Core.Templates;

/// <summary>The ready channel templates (a new template = a new file).</summary>
public interface ITemplateSource
{
    /// <summary>All templates, ordered by category and id.</summary>
    IReadOnlyList<ChannelTemplate> All { get; }

    /// <summary>A template by id; <c>null</c> when unknown.</summary>
    ChannelTemplate? Find(string id);
}
