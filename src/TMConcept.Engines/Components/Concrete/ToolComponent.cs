using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Components;

/// <summary>A tool component that needs no look at the system beyond the installer's own (ffmpeg, sherpa-onnx, stable-diffusion.cpp).</summary>
public sealed class ToolComponent(ToolInstaller installer, string code, string name, IReadOnlyList<ToolKind> tools, IReadOnlyList<string> dependsOn)
    : ToolComponentBase(installer, code, name, tools, dependsOn);
