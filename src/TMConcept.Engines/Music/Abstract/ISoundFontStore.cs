using TMConcept.Core.Tools;

namespace TMConcept.Engines.Music;

/// <summary>
/// The SoundFont of the composed music on this computer (0.1.55, "Program composes"): where it lies, whether it is there, and its download
/// (only after the user agreed).
/// </summary>
public interface ISoundFontStore
{
    /// <summary>The SoundFont's folder.</summary>
    string Folder { get; }

    /// <summary>The SoundFont file.</summary>
    string File { get; }

    /// <summary>Whether the SoundFont is there with its pinned size (files only).</summary>
    bool Ready { get; }

    /// <summary>Downloads the SoundFont (network; only after the user agreed).</summary>
    Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken);
}
