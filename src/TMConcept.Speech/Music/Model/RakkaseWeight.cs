namespace TMConcept.Speech.Music;

/// <summary>One weight file of the local music model (0.1.46; a line of rakkase's pinned <c>scripts/modeller.json</c>).</summary>
/// <param name="Target">Its path inside <c>checkpoints/</c>, as rakkase names it.</param>
/// <param name="File">Its full path on this computer.</param>
/// <param name="Url">Where it comes from (Hugging Face, pinned revision).</param>
/// <param name="Sha256">Its SHA-256.</param>
/// <param name="Bytes">Its size.</param>
public sealed record RakkaseWeight(string Target, string File, Uri Url, string Sha256, long Bytes);
