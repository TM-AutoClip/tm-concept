using Shouldly;
using TMConcept.Engines.References;
using TMConcept.Providers.Channels;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The engine side of "Kanaldan esinlen" (0.1.53): yt-dlp's command line for a channel (only listed, nothing downloaded, no cookies or sign-in,
/// the user's configuration ignored, the newest items only, the address after "--") and the image kinds of the channel photo. yt-dlp is never
/// run; nothing reaches the network.
/// </summary>
public sealed class ChannelListerEngineTests
{
    [Fact]
    public void YtDlpOnlyListsTheChannelWithoutCookies()
    {
        var arguments = YtDlpChannelLister.Arguments(new Uri("https://www.youtube.com/@channel/videos"), 30, string.Empty);

        arguments.ShouldContain("--flat-playlist");
        arguments.ShouldContain("--dump-single-json");
        arguments.ShouldContain("--ignore-config");
        arguments.ShouldContain("--no-plugin-dirs");
        arguments.ShouldContain("--no-cookies");
        arguments.ShouldContain("--no-cookies-from-browser");
        arguments.ShouldNotContain("--cookies");
        arguments.ShouldNotContain("--username");
        arguments.ShouldNotContain("--netrc");
        arguments.ShouldNotContain("-o", "nothing is written");
        arguments.ShouldNotContain("--js-runtimes");
        arguments[arguments.ToList().IndexOf("--playlist-items") + 1].ShouldBe("1:30");
        arguments[^2].ShouldBe("--", "the address can never be read as an option");
        arguments[^1].ShouldBe("https://www.youtube.com/@channel/videos");
        YtDlpChannelLister.Arguments(new Uri("https://example.com/@c"), 0, "node.exe").ShouldContain("node:node.exe");
        YtDlpChannelLister.Arguments(new Uri("https://example.com/@c"), 0, string.Empty).ShouldContain("1:1");
    }

    [Fact]
    public void OnlyImageKindsAreKeptAsTheChannelPhoto()
    {
        ChannelPhotoClient.Extension("image/jpeg").ShouldBe(".jpg");
        ChannelPhotoClient.Extension("IMAGE/PNG").ShouldBe(".png");
        ChannelPhotoClient.Extension("image/webp").ShouldBe(".webp");
        ChannelPhotoClient.Extension("image/svg+xml").ShouldBeNull("a picture that can carry a script is not taken");
        ChannelPhotoClient.Extension("text/html").ShouldBeNull();
        ChannelPhotoClient.Extension(null).ShouldBeNull();
    }
}
