using TMConcept.Core.Components;
using TMConcept.Core.Music;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The local music model's weights as a component (0.1.46, C5; code <see cref="ComponentCodes.MusicModel"/>, after its environment, rule 18:
/// a model is never downloaded by itself): ACE-Step 1.5 (MIT) and its Qwen3-Embedding-0.6B text encoder (Apache-2.0), about 6.4 GB from
/// Hugging Face, each file pinned by rakkase's list.
/// </summary>
public sealed class MusicWeightsComponent(RakkaseInstaller installer, RakkaseLayout layout) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.MusicModel;

    /// <inheritdoc />
    public string Name => "ACE-Step 1.5 music model weights (MIT; Qwen3-Embedding, Apache-2.0)";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.MusicEnvironment];

    /// <inheritdoc />
    public bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <inheritdoc />
    public bool Ready => installer.WeightsReady;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads()
    {
        if (Ready)
        {
            return [];
        }

        // Before the environment is there its list is not on this computer yet: the measured total of rakkase's pinned list.
        long bytes;
        try
        {
            var weights = installer.Weights();
            bytes = weights.Count > 0 ? weights.Where(w => !(File.Exists(w.File) && new FileInfo(w.File).Length == w.Bytes)).Sum(w => w.Bytes)
                : MusicGenerationRules.LocalWeightBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            bytes = MusicGenerationRules.LocalWeightBytes;
        }

        return [new ComponentDownload(Name, RakkasePins.WeightsHost, bytes)];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [layout.Checkpoints];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => installer.InstallWeightsAsync(progress, cancellationToken);
}
