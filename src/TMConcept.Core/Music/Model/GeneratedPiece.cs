namespace TMConcept.Core.Music;

/// <summary>A piece made on the "Müzik üret" page, as it goes into "Müziklerim" (<see cref="IMyMusic.AddGeneratedAsync"/>).</summary>
/// <param name="File">The made file (moved into the collection).</param>
/// <param name="Title">Its name (empty = the file's name).</param>
/// <param name="Model">The model's key ("provider/id").</param>
/// <param name="Prompt">The prompt as the user wrote it.</param>
/// <param name="ReferenceName">The reference sound's file name (empty = none).</param>
/// <param name="Usd">What it cost.</param>
/// <param name="Bpm">The tempo asked for (0 = none).</param>
public sealed record GeneratedPiece(string File, string Title, string Model, string Prompt, string ReferenceName, decimal Usd, int Bpm);
