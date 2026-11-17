using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Documents;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A topic "Find topics" found in the documents, with the documents that cover it and its tick box (one concept per ticked topic). The
/// found topic itself goes into the run, so its concept is searched in its own documents with its search words.
/// </summary>
public sealed partial class DocumentTopicItemViewModel(DocumentTopic topic) : ObservableObject
{
    /// <summary>The topic as found (its documents' ids and its search words go with it).</summary>
    public DocumentTopic Topic { get; } = topic;

    /// <summary>The topic (one line).</summary>
    public string Text => Topic.Text;

    /// <summary>The documents that cover it ("Byzantine trade; The Nika riots"), or empty.</summary>
    public string Documents { get; } = string.Join("; ", topic.Documents);

    /// <summary>Ticked: "Write from the ticked" writes one concept for it.</summary>
    [ObservableProperty]
    public partial bool Checked { get; set; } = true;
}
