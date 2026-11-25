namespace TMConcept.Core.Music;

/// <summary>
/// A piece the user asks for on the "Müzik üret" page (0.1.54): the music model, the prompt as written (free: "antik Mısır'da disko"), the
/// length asked for (a model with a fixed length ignores it), without words or not, an optional reference sound (its tempo is measured and
/// asked for) and the name the piece gets in the collection.
/// </summary>
/// <param name="Model">The music model (local or online).</param>
/// <param name="Prompt">The prompt.</param>
/// <param name="Seconds">The length asked for.</param>
/// <param name="Instrumental">Without words.</param>
/// <param name="ReferenceFile">A sound whose tempo the piece follows; null = none.</param>
/// <param name="Title">The piece's name in "Müziklerim" (empty = from the prompt).</param>
public sealed record MusicMakeRequest(Models.CatalogModel Model, string Prompt, int Seconds, bool Instrumental, string? ReferenceFile, string Title);
