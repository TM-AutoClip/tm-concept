using TMConcept.Core.Tools;

namespace TMConcept.Engines.Images;

/// <summary>One pinned weights file of a local image model and what it is (<see cref="ImageFileRole"/>: which sd-cli option names it).</summary>
/// <param name="Role">What the file is.</param>
/// <param name="File">The pinned download (Hugging Face commit, SHA-256, size).</param>
public sealed record ImageModelFile(ImageFileRole Role, ToolArchive File);
