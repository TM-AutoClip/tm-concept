using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// Downloads on a bad network (the developer's report, 16 Nov 2026: "The SSL connection could not be established" ended a 12 GB image model
/// download, which then started from zero): tries again with a pause, continues the kept part with an HTTP Range request, a time-out is
/// per read and is not a cancel, and the final failure says what kind of network problem it was with the system's own reason.
/// </summary>
public sealed class DownloadResumeTests : IDisposable
{
    private static readonly DownloadPolicy Quick = new(3, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(300));

    private readonly TempPaths _paths = new();
    private readonly byte[] _body = RandomNumberGenerator.GetBytes(50_000);

    private string File => Path.Combine(_paths.DataDirectory, "model.bin");

    private ToolArchive Archive => ToolManifest.Archives.First(a => a.Tool == ToolKind.Node && a.Rid == "win-x64") with
    {
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(_body)),
    };

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task ACutConnectionIsContinuedWithARangeRequestAndTheWholeFileIsChecked()
    {
        var server = new FlakyServer(_body);
        server.Script.Enqueue(server.CutAfter(12_000));

        await Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None);

        System.IO.File.ReadAllBytes(File).ShouldBe(_body);
        server.Ranges.ShouldBe([null, 12_000], "the second try asked only for the rest");
        System.IO.File.Exists(File + ".part").ShouldBeFalse();
    }

    [Fact]
    public async Task WhenEveryTryFailsThePartIsKeptAndTheNextInstallContinuesIt()
    {
        var server = new FlakyServer(_body);
        server.Script.Enqueue(server.CutAfter(10_000));
        server.Script.Enqueue(server.CutAfter(5_000));
        server.Script.Enqueue(server.CutAfter(5_000));

        var error = await Should.ThrowAsync<ToolInstallException>(() => Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None));

        error.Code.ShouldBe("connection");
        error.Detail.ShouldNotBeEmpty("the system's own reason (here the reset socket's text, in the system's language)");
        error.Message.ShouldContain("forcibly closed");
        new FileInfo(File + ".part").Length.ShouldBe(20_000, "what came in three tries is kept");
        server.Ranges.ShouldBe([null, 10_000, 15_000]);

        // Days later: the next install starts at byte 20 000.
        await Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None);
        server.Ranges[^1].ShouldBe(20_000);
        System.IO.File.ReadAllBytes(File).ShouldBe(_body);
    }

    [Fact]
    public async Task ATlsFailureIsRetriedThenNamedWithItsInnerReason()
    {
        var server = new FlakyServer(_body);
        for (var i = 0; i < Quick.Attempts; i++)
        {
            server.Script.Enqueue(FlakyServer.Throw(new HttpRequestException(HttpRequestError.SecureConnectionError,
                "The SSL connection could not be established, see inner exception.",
                new AuthenticationException("The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot"))));
        }

        var error = await Should.ThrowAsync<ToolInstallException>(() => Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None));

        error.Code.ShouldBe("tls");
        error.Detail.ShouldBe("The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot");
        error.Message.ShouldContain("after 3 tries");
        server.Ranges.Count.ShouldBe(Quick.Attempts);
    }

    [Fact]
    public async Task AnSslHiccupOnceDoesNotEndTheDownload()
    {
        var server = new FlakyServer(_body);
        server.Script.Enqueue(FlakyServer.Throw(new HttpRequestException(HttpRequestError.SecureConnectionError, "The SSL connection could not be established.",
            new IOException("Received an unexpected EOF or 0 bytes from the transport stream."))));

        await Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None);

        System.IO.File.ReadAllBytes(File).ShouldBe(_body);
    }

    [Fact]
    public async Task AnUnknownHostIsADnsFailure()
    {
        var server = new FlakyServer(_body);
        for (var i = 0; i < Quick.Attempts; i++)
        {
            server.Script.Enqueue(FlakyServer.Throw(new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known. (nodejs.org:443)",
                new SocketException((int)SocketError.HostNotFound))));
        }

        var error = await Should.ThrowAsync<ToolInstallException>(() => Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None));

        error.Code.ShouldBe("dns");
    }

    [Fact]
    public async Task AServerThatStopsSendingTimesOutPerReadAndThatIsNotACancel()
    {
        var server = new FlakyServer(_body);
        for (var i = 0; i < Quick.Attempts; i++)
        {
            server.Script.Enqueue(server.Stall());
        }

        var error = await Should.ThrowAsync<ToolInstallException>(() => Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None));

        error.Code.ShouldBe("timeout");
        error.Detail.ShouldContain("No data arrived");
    }

    [Fact]
    public async Task TheUsersCancelStaysACancelAndKeepsThePart()
    {
        var server = new FlakyServer(_body);
        server.Script.Enqueue(server.CutAfter(8_000));
        using var cancel = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.Share > 0.1)
            {
                cancel.Cancel();
            }
        });
        var slow = new DownloadPolicy(3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<OperationCanceledException>(() => Downloader(server, slow).DownloadAsync(Archive, File, progress, cancel.Token));

        System.IO.File.Exists(File + ".part").ShouldBeTrue("a cancelled download continues next time");
    }

    [Fact]
    public async Task AServerThatIgnoresTheRangeSendsTheWholeFileAndThePartStartsAgain()
    {
        var server = new FlakyServer(_body) { HonourRange = false };
        System.IO.File.WriteAllBytes(File + ".part", RandomNumberGenerator.GetBytes(7_000));

        await Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None);

        server.Ranges.ShouldBe([7_000]);
        System.IO.File.ReadAllBytes(File).ShouldBe(_body);
    }

    [Fact]
    public async Task APartThatIsNotTheFilesBeginningFailsTheHashAndIsDeleted()
    {
        var server = new FlakyServer(_body);
        System.IO.File.WriteAllBytes(File + ".part", RandomNumberGenerator.GetBytes(7_000));

        var error = await Should.ThrowAsync<ToolInstallException>(() => Downloader(server).DownloadAsync(Archive, File, null, CancellationToken.None));

        error.Code.ShouldBe("hash");
        System.IO.File.Exists(File + ".part").ShouldBeFalse("a wrong part is not continued again");
    }

    [Fact]
    public async Task ABusyServerIsTriedAgainAndARefusalIsNot()
    {
        var busy = new FlakyServer(_body);
        busy.Script.Enqueue(FlakyServer.Status(HttpStatusCode.ServiceUnavailable));
        await Downloader(busy).DownloadAsync(Archive, File, null, CancellationToken.None);
        busy.Ranges.Count.ShouldBe(2);

        System.IO.File.Delete(File);
        var refused = new FlakyServer(_body);
        refused.Script.Enqueue(FlakyServer.Status(HttpStatusCode.Forbidden));
        var error = await Should.ThrowAsync<ToolInstallException>(() => Downloader(refused).DownloadAsync(Archive, File, null, CancellationToken.None));
        error.Code.ShouldBe("download");
        error.Detail.ShouldBe("HTTP 403");
        refused.Ranges.Count.ShouldBe(1);
    }

    [Fact]
    public void TheRetryPausesGrowUpToTheirLimit()
    {
        var policy = DownloadPolicy.Default;
        policy.DelayAfter(1).ShouldBe(TimeSpan.FromSeconds(2));
        policy.DelayAfter(2).ShouldBe(TimeSpan.FromSeconds(4));
        policy.DelayAfter(4).ShouldBe(TimeSpan.FromSeconds(16));
        policy.DelayAfter(9).ShouldBe(TimeSpan.FromSeconds(30));
    }

    private static ToolDownloader Downloader(FlakyServer server, DownloadPolicy? policy = null) =>
        new(new DownloadTransport(() => server), NullLogger<ToolDownloader>.Instance, policy ?? Quick);

    /// <summary>A progress that reports on the calling thread (Progress&lt;T&gt; posts later).</summary>
    private sealed class SyncProgress(Action<ToolProgress> report) : IProgress<ToolProgress>
    {
        public void Report(ToolProgress value) => report(value);
    }
}
