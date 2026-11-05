using TMConcept.Core.Models;

namespace TMConcept.Core.Images;

/// <summary>A prompt, the wanted size, the output file (png/jpg) and the resolved model with its key.</summary>
public sealed record ImageRequest(string Prompt, int Width, int Height, string Output, ResolvedModel Model);
