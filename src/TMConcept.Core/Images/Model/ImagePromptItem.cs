namespace TMConcept.Core.Images;

/// <summary>One image prompt of the answer: the scene number it is for and the prompt (<see cref="ImagePromptBuilder.Read"/>).</summary>
public sealed record ImagePromptItem(int Scene, string Prompt);
