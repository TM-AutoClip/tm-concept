using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Tools;
using TMConcept.Documents.Ocr;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// 0.1.27: one pinned entry of a published zip (a NuGet package) is fetched with a range request for exactly its bytes, unpacked, and moved
/// into place only when the unpacked file's size and SHA-256 match; a server that sends the whole package instead, a host or a redirect
/// outside the allowed list, or a wrong hash leave nothing behind. A zip made by the test, a fake server: nothing goes to a network.
/// </summary>
public sealed class ZipEntryDownloaderTests : IDisposable
{
    private static readonly DownloadPolicy Quick = new(2, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(5));

    private readonly TempPaths _paths = new();
    private readonly byte[] _library = RandomNumberGenerator.GetBytes(40_000);
    private readonly byte[] _package;
    private readonly ZipEntryArchive _entry;

    public ZipEntryDownloaderTests()
    {
        // A package with a few entries; the pinned one is in the middle, stored with deflate.
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "LICENSE", "MIT"u8.ToArray());
            Add(zip, "runtimes/win-x64/native/onnxruntime.dll", _library);
            Add(zip, "ThirdPartyNotices.txt", RandomNumberGenerator.GetBytes(3_000));
        }

        _package = stream.ToArray();
        var (offset, compressed) = Locate(_package, "runtimes/win-x64/native/onnxruntime.dll");
        _entry = new ZipEntryArchive(ToolKind.OcrGpuRuntime, "1.24.4", new Uri("https://api.nuget.org/v3-flatcontainer/x/1.0/x.1.0.nupkg"), offset, compressed, true,
            Convert.ToHexStringLower(SHA256.HashData(_library)), "onnxruntime.dll", _library.Length);
    }

    private string Target => Path.Combine(_paths.DataDirectory, "onnxruntime.dll");

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task OnlyTheEntrysBytesAreFetchedAndTheUnpackedFileIsChecked()
    {
        var server = new PackageServer(_package);
        var shares = new List<double>();
        Directory.CreateDirectory(_paths.DataDirectory);

        await Downloader(server).DownloadAsync(_entry, Target, new Sync(p => shares.Add(p.Share)), CancellationToken.None);

        File.ReadAllBytes(Target).ShouldBe(_library);
        server.Ranges.ShouldBe([(_entry.DataOffset, _entry.DataOffset + _entry.CompressedBytes - 1)]);
        File.Exists(Target + ".part").ShouldBeFalse();
        File.Exists(Target + ".zpart").ShouldBeFalse();
        shares.ShouldNotBeEmpty();
        shares[^1].ShouldBe(1);
    }

    [Fact]
    public async Task AWrongHashLeavesNothingBehind()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var wrong = _entry with { Sha256 = new string('0', 64) };

        var error = await Should.ThrowAsync<ToolInstallException>(() => Downloader(new PackageServer(_package)).DownloadAsync(wrong, Target, null, CancellationToken.None));

        error.Code.ShouldBe("hash");
        Directory.GetFiles(_paths.DataDirectory).ShouldBeEmpty();
    }

    [Fact]
    public async Task AServerThatSendsTheWholePackageIsRefused()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var server = new PackageServer(_package) { IgnoreRange = true };

        var error = await Should.ThrowAsync<ToolInstallException>(() => Downloader(server).DownloadAsync(_entry, Target, null, CancellationToken.None));

        error.Code.ShouldBe("download");
        Directory.GetFiles(_paths.DataDirectory).ShouldBeEmpty();
    }

    [Fact]
    public async Task HostsAndRedirectsOutsideTheListAreRefused()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var elsewhere = _entry with { Url = new Uri("https://example.com/x.nupkg") };
        var direct = await Should.ThrowAsync<ToolInstallException>(() => Downloader(new PackageServer(_package)).DownloadAsync(elsewhere, Target, null, CancellationToken.None));
        direct.Code.ShouldBe("host");

        var redirected = new PackageServer(_package) { RedirectTo = new Uri("https://example.com/x.nupkg") };
        var hop = await Should.ThrowAsync<ToolInstallException>(() => Downloader(redirected).DownloadAsync(_entry, Target, null, CancellationToken.None));
        hop.Code.ShouldBe("host");
        File.Exists(Target).ShouldBeFalse();
    }

    [Fact]
    public void TheDirectMlPinsAreWholeAndComeFromAnAllowedHost()
    {
        DirectMlRuntimeRelease.Files.Count.ShouldBe(5);
        foreach (var file in DirectMlRuntimeRelease.Files)
        {
            file.Sha256.Length.ShouldBe(64);
            file.Sha256.ShouldAllBe(c => Uri.IsHexDigit(c));
            file.CompressedBytes.ShouldBeGreaterThan(0);
            file.Bytes.ShouldBeGreaterThanOrEqualTo(file.CompressedBytes);
            file.DataOffset.ShouldBeGreaterThan(0);
            file.Url.Scheme.ShouldBe("https");
            ToolManifest.AllowedHosts.ShouldContain(file.Url.Host);
        }

        // A folder never holds a library without its licence: the licence texts come first.
        DirectMlRuntimeRelease.Files.TakeWhile(f => !f.FileName.EndsWith(".dll", StringComparison.Ordinal)).Count().ShouldBe(3);
        DirectMlRuntimeRelease.Files.Select(f => f.FileName).ShouldBeUnique();
    }

    private ZipEntryDownloader Downloader(PackageServer server) =>
        new(new DownloadTransport(() => server), NullLogger<ZipEntryDownloader>.Instance, Quick);

    private static void Add(ZipArchive zip, string name, byte[] data)
    {
        using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        entry.Write(data);
    }

    /// <summary>Where an entry's data starts in the zip and how long it is (its local header read by hand, as the pins were made).</summary>
    private static (long Offset, long Compressed) Locate(byte[] zip, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entries = archive.Entries.ToList();
        var position = 0;
        while (true)
        {
            BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(position)).ShouldBe(0x04034b50u);
            var compressed = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(position + 18));
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(position + 26));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(position + 28));
            var entryName = System.Text.Encoding.UTF8.GetString(zip, position + 30, nameLength);
            var data = position + 30 + nameLength + extraLength;
            var size = compressed != 0 ? compressed : entries.Single(e => e.FullName == entryName).CompressedLength;
            if (entryName == name)
            {
                return (data, size);
            }

            // .NET writes a data descriptor after each entry (16 bytes with its signature).
            position = (int)(data + size);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(position)) == 0x08074b50u)
            {
                position += 16;
            }
        }
    }

    private sealed class Sync(Action<ToolProgress> report) : IProgress<ToolProgress>
    {
        public void Report(ToolProgress value) => report(value);
    }

    /// <summary>Serves the package from api.nuget.org, honouring a byte range (or not).</summary>
    private sealed class PackageServer(byte[] package) : HttpMessageHandler
    {
        public List<(long From, long To)> Ranges { get; } = [];

        public bool IgnoreRange { get; init; }

        public Uri? RedirectTo { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (RedirectTo is { } next && request.RequestUri!.Host != next.Host)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = next;
                return Task.FromResult(redirect);
            }

            var range = request.Headers.Range!.Ranges.Single();
            Ranges.Add((range.From!.Value, range.To!.Value));
            if (IgnoreRange)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });
            }

            var from = (int)range.From.Value;
            var to = (int)range.To.Value;
            var content = new ByteArrayContent(package[from..(to + 1)]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, package.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        }
    }
}
