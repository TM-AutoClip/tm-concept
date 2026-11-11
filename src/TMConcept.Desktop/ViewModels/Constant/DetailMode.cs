namespace TMConcept.Desktop.ViewModels;

/// <summary>What the concept detail panel shows.</summary>
public enum DetailMode
{
    /// <summary>No concept is chosen: a line says to click one.</summary>
    Empty = 0,

    /// <summary>The chosen concept's full text and its actions.</summary>
    Viewing = 1,

    /// <summary>Title, summary and text in editors (also an AI result waiting for Save).</summary>
    Editing = 2,

    /// <summary>The box for an AI edit: what should change, the estimate, Send.</summary>
    AiEditing = 3,
}
