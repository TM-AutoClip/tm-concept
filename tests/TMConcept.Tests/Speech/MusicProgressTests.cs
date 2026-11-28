using System.Diagnostics;
using System.Text.RegularExpressions;
using Shouldly;
using TMConcept.Core.Music;
using TMConcept.Speech.Music;
using TMConcept.Speech.Python;

namespace TMConcept.Tests.Speech;

/// <summary>
/// 0.1.61 (plan K14): the local music scripts' progress lines ("TMC-P: 2/3 16/40 blocks") read into one progress model, what the log takes
/// (every 10 % of a stage), and the scripts' own printer (<c>tmc_progress.py</c>, run with the system's Python on synthetic counts: no model,
/// no library, no card). Every unit word a script prints is one the reader knows.
/// </summary>
public sealed partial class MusicProgressTests
{
    private readonly MusicProgressLineParser _parser = new();

    [Theory]
    [InlineData("TMC-P: 2/3 16/40 blocks", 2, 3, 16, 40, MusicProgressUnit.Blocks)]
    [InlineData("TMC-P: 1/3 1240/6000 tokens", 1, 3, 1240, 6000, MusicProgressUnit.Tokens)]
    [InlineData("TMC-P: 1/1 50/50 steps", 1, 1, 50, 50, MusicProgressUnit.Steps)]
    [InlineData("TMC-P: 3/3 0/0 decoding", 3, 3, 0, 0, MusicProgressUnit.Decoding)]
    [InlineData("TMC-P:  4/4  3/8  decoding ", 4, 4, 3, 8, MusicProgressUnit.Decoding)]
    public void AProgressLineIsReadIntoStageCountAndUnit(string line, int stage, int stages, long done, long total, MusicProgressUnit unit) =>
        _parser.Parse(line).ShouldBe(new MusicProgress(stage, stages, done, total, unit));

    [Theory]
    [InlineData("TMC: stage 1 (exllamav2)")]
    [InlineData("TMC-E-MEMORY: the graphics card ran out of memory")]
    [InlineData("100%|##########| 3000/3000 [01:02<00:00]")]
    [InlineData("TMC-P: 2/3 16/40 seconds")]
    [InlineData("TMC-P: 4/3 1/2 tokens")]
    [InlineData("TMC-P: 0/3 1/2 tokens")]
    [InlineData("TMC-P: 1/3 41/40 blocks")]
    [InlineData("TMC-P: 1/3 5/0 tokens")]
    [InlineData("TMC-P: 1/3 -1/40 blocks")]
    [InlineData("TMC-P: 1/3 16/40")]
    [InlineData("TMC-P: 1/3 a/40 blocks")]
    [InlineData("")]
    public void AnythingElseIsNotAProgressLine(string line) => _parser.Parse(line).ShouldBeNull();

    [Fact]
    public void TheShareIsTheRunningStagesAndTheLogTakesEveryTenPerCent()
    {
        var blocks = new MusicProgress(2, 3, 16, 40, MusicProgressUnit.Blocks);
        blocks.Percent.ShouldBe(40);
        blocks.Counted.ShouldBeTrue();
        blocks.Staged.ShouldBeTrue();
        MusicProgress.Unknown.Counted.ShouldBeFalse();
        MusicProgress.Unknown.Staged.ShouldBeFalse();
        new MusicProgress(3, 3, 0, 0, MusicProgressUnit.Decoding).Percent.ShouldBe(0);

        blocks.LogsAfter(null).ShouldBeTrue();
        (blocks with { Done = 19 }).LogsAfter(blocks).ShouldBeFalse("47 % is still the 40s");
        (blocks with { Done = 20 }).LogsAfter(blocks).ShouldBeTrue("50 %");
        (blocks with { Stage = 3, Done = 0, Total = 0, Unit = MusicProgressUnit.Decoding }).LogsAfter(blocks).ShouldBeTrue("a new stage");
    }

    [Fact]
    public void EveryUnitAScriptPrintsIsOneTheReaderKnows()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var script in PythonFiles.Scripts.Where(s => s.StartsWith("music_", StringComparison.Ordinal)))
        {
            var text = System.Text.Encoding.UTF8.GetString(PythonFiles.Read(script));
            foreach (Match match in BeginUnit().Matches(text))
            {
                used.Add(match.Groups["unit"].Value);
            }
        }

        used.ShouldBe(["tokens", "blocks", "steps", "decoding"], ignoreOrder: true);
        used.ShouldAllBe(u => MusicProgressLineParser.Units.ContainsKey(u));
    }

    [Fact]
    public void ThePrinterWritesLinesTheReaderReadsAtMostOnePerPerCentOrSecond()
    {
        Assert.SkipUnless(Python() is not null, "Python 3 is not installed.");
        var folder = Directory.CreateTempSubdirectory("tmc-progress-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "tmc_progress.py"), PythonFiles.Read("tmc_progress.py"));

            // A fake clock: 6000 tokens, 10 ms each (a whole per cent every 0.6 s), then 30 slow steps of 1.5 s, then a decoding without a count.
            var output = Run(folder, """
                import sys
                sys.path.insert(0, sys.argv[1])
                from tmc_progress import Reporter
                now = [0.0]
                r = Reporter(3, clock=lambda: now[0])
                r.begin(1, "tokens", 6000)
                for i in range(1, 6001):
                    now[0] += 0.01
                    r.update(i)
                r.finish()
                r.begin(2, "blocks", 3000)
                for i in range(1, 31):
                    now[0] += 1.5
                    r.update(i)
                r.finish()
                r.begin(3, "decoding")
                r.finish()
                """);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var read = lines.Select(_parser.Parse).ToList();

            read.ShouldAllBe(p => p != null, "every printed line is a progress line");
            var tokens = read.Where(p => p!.Stage == 1).ToList();
            tokens.Count.ShouldBe(101, "0 % and every whole per cent: not one line per token");
            tokens[^1]!.Done.ShouldBe(6000);
            var slow = read.Where(p => p!.Stage == 2).ToList();
            slow.Count.ShouldBe(32, "the start, once a second while the count moves (1 % is 30 blocks here), the end");
            slow[^1]!.Done.ShouldBe(3000, "the stage's last line shows its whole length");
            read[^1].ShouldBe(new MusicProgress(3, 3, 0, 0, MusicProgressUnit.Decoding));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [GeneratedRegex("""(?:\.begin\(\s*[^,()]+,\s*|:\s*\(\d+,\s*)"(?<unit>[a-z]+)"\)?""")]
    private static partial Regex BeginUnit();

    private static string Run(string folder, string code)
    {
        var info = new ProcessStartInfo(Python()!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-I");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(code);
        info.ArgumentList.Add(folder);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, error);
        return output;
    }

    private static string? Python()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "python.exe" } : ["python3", "python"];
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(d => names.Select(n => Path.Combine(d, n)))
            .FirstOrDefault(File.Exists);
    }
}
