using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Tools;
using TMConcept.Engines.Images;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The downloader of big model files: a broken transfer is retried and resumed from what arrived (Range), the hash covers the whole file,
/// a whole fresh mismatch is final, a gone file is not retried. A fake server only; nothing goes to the network.
/// </summary>
public sealed class ResumingDownloadTests
{
    private static readonly byte[] Body = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7 % 251)).ToArray();

    private static ToolArchive Archive(byte[] body) => HuggingFaceFile.Pinned("someone/model", new string('a', 40), "weights.gguf",
        Convert.ToHexStringLower(SHA256.HashData(body)), body.Length);

    private static ResumingDownloader Downloader(FlakyServer server) =>
        new(new DownloadTransport(() => server), NullLogger<ResumingDownloader>.Instance) { FirstDelay = TimeSpan.Zero, Attempts = 4 };

    [Fact]
    public async Task ABrokenTransferIsResumedFromWhatArrivedThroughTheRedirect()
    {
        using var paths = new TempPaths();
        var server = new FlakyServer(Body) { BreakAfter = [100_000, 120_000] };
        var file = Path.Combine(paths.DataDirectory, "weights.gguf");
        using var downloader = Downloader(server);

        await downloader.DownloadAsync(Archive(Body), file, null, CancellationToken.None);

        File.ReadAllBytes(file).ShouldBe(Body);
        File.Exists(file + ".part").ShouldBeFalse();
        server.Ranges.ShouldBe([null, 100_000L, 220_000L], "each attempt asks only for the rest");
        server.Hosts.ShouldAllBe(h => h == "huggingface.co" || h == "cas-bridge.xethub.hf.co");
    }

    [Fact]
    public async Task AServerThatIgnoresTheRangeStartsTheFileAgain()
    {
        using var paths = new TempPaths();
        var server = new FlakyServer(Body) { BreakAfter = [50_000], IgnoreRange = true };
        var file = Path.Combine(paths.DataDirectory, "weights.gguf");
        using var downloader = Downloader(server);

        await downloader.DownloadAsync(Archive(Body), file, null, CancellationToken.None);

        File.ReadAllBytes(file).ShouldBe(Body);
    }

    [Fact]
    public async Task APartLeftFromAnEarlierRunIsContinued()
    {
        using var paths = new TempPaths();
        var file = Path.Combine(paths.DataDirectory, "weights.gguf");
        File.WriteAllBytes(file + ".part", Body[..200_000]);
        var server = new FlakyServer(Body);
        using var downloader = Downloader(server);

        await downloader.DownloadAsync(Archive(Body), file, null, CancellationToken.None);

        File.ReadAllBytes(file).ShouldBe(Body);
        server.Ranges.ShouldBe([200_000L]);
    }

    [Fact]
    public async Task ADamagedKeptPartIsDroppedAndTheFileFetchedWhole()
    {
        using var paths = new TempPaths();
        var file = Path.Combine(paths.DataDirectory, "weights.gguf");
        var damaged = Body[..200_000];
        damaged[10] ^= 0xFF;
        File.WriteAllBytes(file + ".part", damaged);
        var server = new FlakyServer(Body);
        using var downloader = Downloader(server);

        await downloader.DownloadAsync(Archive(Body), file, null, CancellationToken.None);

        File.ReadAllBytes(file).ShouldBe(Body);
        server.Ranges.ShouldBe([200_000L, null]);
    }

    [Fact]
    public async Task AWrongFileAGoneFileAndTooManyBreaksAreFinal()
    {
        using var paths = new TempPaths();
        var file = Path.Combine(paths.DataDirectory, "weights.gguf");

        using (var wrong = Downloader(new FlakyServer(Body)))
        {
            var error = await Should.ThrowAsync<ToolInstallException>(() => wrong.DownloadAsync(Archive(Body) with { Sha256 = new string('0', 64) }, file, null,
                CancellationToken.None));
            error.Code.ShouldBe("hash");
            File.Exists(file + ".part").ShouldBeFalse();
        }

        var gone = new FlakyServer(Body) { Status = HttpStatusCode.NotFound };
        using (var downloader = Downloader(gone))
        {
            (await Should.ThrowAsync<ToolInstallException>(() => downloader.DownloadAsync(Archive(Body), file, null, CancellationToken.None))).Code.ShouldBe("gone");
            gone.Ranges.Count.ShouldBe(1, "a gone file is not asked again");
        }

        var flaky = new FlakyServer(Body) { BreakAfter = [10, 10, 10, 10, 10] };
        using (var downloader = Downloader(flaky))
        {
            var error = await Should.ThrowAsync<ToolInstallException>(() => downloader.DownloadAsync(Archive(Body), file, null, CancellationToken.None));
            error.Code.ShouldBe("download");
            error.Message.ShouldContain("4 attempts");
            File.Exists(file + ".part").ShouldBeTrue("what arrived is kept for the next try");
        }
    }

    /// <summary>
    /// huggingface.co answers with a redirect to its file server, which sends the file (or the asked range); <see cref="BreakAfter"/> ends
    /// each of the first answers after that many bytes with an I/O error.
    /// </summary>
    private sealed class FlakyServer(byte[] body) : HttpMessageHandler
    {
        private int _answer;

        public List<long> BreakAfter { get; init; } = [];

        public bool IgnoreRange { get; init; }

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public List<long?> Ranges { get; } = [];

        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hosts.Add(request.RequestUri!.Host);
            if (request.RequestUri.Host == "huggingface.co")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://cas-bridge.xethub.hf.co/file?signed=1");
                return Task.FromResult(redirect);
            }

            var from = request.Headers.Range?.Ranges.First().From;
            Ranges.Add(from);
            if (Status != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(Status));
            }

            var start = IgnoreRange ? 0 : from ?? 0;
            var rest = body[(int)start..];
            var cut = _answer < BreakAfter.Count ? BreakAfter[_answer] : -1;
            _answer++;
            var response = new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(new BreakingStream(rest, cut)),
            };
            response.Content.Headers.ContentLength = rest.Length;
            if (start > 0)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, body.Length - 1, body.Length);
            }

            return Task.FromResult(response);
        }
    }

    /// <summary>Gives its bytes, but fails with an I/O error after <c>cut</c> of them (negative: never).</summary>
    private sealed class BreakingStream(byte[] data, long cut) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => Check(base.Read(buffer, offset, Limit(count)));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Check(base.Read(buffer.Span[..Limit(buffer.Length)])));

        private int Limit(int count) => cut < 0 ? count : (int)Math.Max(0, Math.Min(count, cut - Position));

        private int Check(int read) => cut >= 0 && Position >= cut && read == 0 ? throw new IOException("The connection was reset.") : read;
    }
}
