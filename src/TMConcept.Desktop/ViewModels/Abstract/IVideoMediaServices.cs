namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// What the profile's music and reference video choices need (0.1.54; 0.1.55 behind this interface): the file dialogs, the ready library with
/// the Openverse search and its install, the reference measure with what it needs installed, and the music models with how the local ones fit
/// this card (read once, off the window's thread). A music model itself is installed when it starts, through <see cref="Components.IModelStartGate"/>,
/// never from here. Shared by the video page's model choice and the music page.
/// </summary>
public interface IVideoMediaServices
{
    /// <summary>The library music's services.</summary>
    MusicPickerServices Picker { get; }

    /// <summary>The reference video's services.</summary>
    ReferenceVideoServices Reference { get; }

    /// <summary>Asks for an audio file (null when canceled).</summary>
    Task<string?> PickMusicFileAsync();

    /// <summary>The music models, whether each can be used now and how the local ones fit this card.</summary>
    Task<MusicGeneratorServices> GeneratorsAsync();
}
