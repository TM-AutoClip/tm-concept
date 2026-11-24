namespace TMConcept.Core.Music;

/// <summary>
/// The scenes the music plays under (0.1.45, the scene plan's hints; <see cref="MusicWindows.Of"/>): from the start of
/// <see cref="FirstScene"/> to the end of <see cref="LastScene"/> (0-based), coming in and going out at once or faded.
/// </summary>
/// <param name="FirstScene">The scene the music starts with.</param>
/// <param name="LastScene">The scene the music ends with.</param>
/// <param name="FadeIn">The music fades in.</param>
/// <param name="FadeOut">The music fades out before the video's end (a music that ends with the video takes the template's fade-out).</param>
public sealed record MusicWindow(int FirstScene, int LastScene, bool FadeIn, bool FadeOut);
