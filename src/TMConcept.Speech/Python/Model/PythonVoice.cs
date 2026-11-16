using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Python;

/// <summary>
/// A local voice that runs on PyTorch in a Python worker (<see cref="PythonWorkerClient"/>): its model files (pinned by Hugging Face commit and
/// SHA-256, downloaded only after a confirmation into <c>&lt;tools&gt;/speech-py/models/&lt;code&gt;</c>), the environment it runs in, the
/// adapter script that speaks with it and what the step shows.
/// </summary>
/// <param name="Code">Code (<see cref="SpeechEngines"/>; the catalog id of the local model).</param>
/// <param name="Name">Display name.</param>
/// <param name="Environment">The <see cref="PythonEnvironment.Key"/> it runs in.</param>
/// <param name="Adapter">The adapter script (<c>adapter_&lt;name&gt;.py</c>).</param>
/// <param name="Files">The model files (target path relative to the model folder in <see cref="ToolArchive.ExecutablePath"/>).</param>
/// <param name="Languages">Content languages it speaks.</param>
/// <param name="Voices">Its own voices (built-in speakers, emotions).</param>
/// <param name="CopiesVoice">It also speaks with the user's own voice samples (Settings → Voice and pronunciation).</param>
/// <param name="OnCard">It uses an NVIDIA card through CUDA when there is one (false: always the processor).</param>
/// <param name="NeedMegabytes">Free graphics memory it needs on the card; with less it runs on the processor.</param>
/// <param name="Options">What the adapter is told when it loads.</param>
/// <param name="BaseVoice">The voice whose model folder it builds on (Ahenk is a LoRA on VoxCPM2); null when none.</param>
public sealed record PythonVoice(string Code, string Name, string Environment, string Adapter, IReadOnlyList<ToolArchive> Files,
    IReadOnlyList<string> Languages, IReadOnlyList<SpeechVoice> Voices, bool CopiesVoice, bool OnCard, int NeedMegabytes,
    IReadOnlyDictionary<string, string> Options, string? BaseVoice = null);
