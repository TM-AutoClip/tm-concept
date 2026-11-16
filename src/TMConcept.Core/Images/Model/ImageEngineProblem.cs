namespace TMConcept.Core.Images;

/// <summary>
/// Why an image engine cannot make pictures on this computer now (<see cref="IImageEngine.Problem"/>), found before anything is paid for.
/// </summary>
/// <param name="Code">The video failure code it stops a video with (<see cref="ImageEngineProblems"/>; the screen words it).</param>
/// <param name="Detail">What is missing, in English (the log and the end of the on-screen reason).</param>
public sealed record ImageEngineProblem(string Code, string Detail);
