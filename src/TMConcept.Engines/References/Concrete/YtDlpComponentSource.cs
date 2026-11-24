using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Engines.Components;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.References;

/// <summary>The component of the reference video download (0.1.52, S1): yt-dlp, installed only after the user's yes (rule 18).</summary>
public sealed class YtDlpComponentSource(ToolInstaller installer) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _components = new(() =>
        [new ToolComponent(installer, ComponentCodes.VideoDownloader, "yt-dlp", [ToolKind.VideoDownloader], [ComponentCodes.Ffmpeg])]);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components.Value;
}
