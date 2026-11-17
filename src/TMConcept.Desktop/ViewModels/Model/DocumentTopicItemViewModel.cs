using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A topic "Find topics" found in the documents, with the documents that cover it and its tick box (one concept per ticked topic).</summary>
public sealed partial class DocumentTopicItemViewModel(string text, string documents) : ObservableObject
{
    /// <summary>The topic (one line).</summary>
    public string Text { get; } = text;

    /// <summary>The documents that cover it ("Byzantine trade; The Nika riots"), or empty.</summary>
    public string Documents { get; } = documents;

    /// <summary>Ticked: "Write from the ticked" writes one concept for it.</summary>
    [ObservableProperty]
    public partial bool Checked { get; set; } = true;
}
