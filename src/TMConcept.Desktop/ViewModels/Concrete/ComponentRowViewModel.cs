using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Components;

namespace TMConcept.Desktop.ViewModels;

/// <summary>An installed component in Settings → General: name, size, and whether it can be removed or render a sample.</summary>
public sealed partial class ComponentRowViewModel(IComponent component, string size, bool canSample) : ObservableObject
{
    /// <summary>The component.</summary>
    public IComponent Component { get; } = component;

    /// <summary>Name.</summary>
    public string Name => Component.Name;

    /// <summary>Size on disk ("312 MB", or "system" for a copy of the system).</summary>
    public string Size { get; } = size;

    /// <summary>Whether Remove is shown (the app's own copy).</summary>
    public bool CanRemove => Component.Folders().Count > 0;

    /// <summary>Whether the sample render button is shown (an engine).</summary>
    public bool CanSample { get; } = canSample;

    /// <summary>Working.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }
}
