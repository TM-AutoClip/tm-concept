using System.Diagnostics;
using Shouldly;

namespace TMConcept.Tests.Architecture;

/// <summary>The repository's own secret scanner (run by the pre-commit hook) stops a leak and lets ordinary text through.</summary>
public sealed class HookTests
{
    public static TheoryData<string> Leaks() => new()
    {
        "key = \"sk-ant-" + new string('a', 30) + "\"",
        "C:" + "\\Users\\someone\\Documents\\file.txt",
        "-----BEGIN " + "PRIVATE KEY-----",
        "contact: some.person" + "@gmail.com",
    };

    [Theory]
    [MemberData(nameof(Leaks))]
    public void ScannerStopsALeak(string line)
    {
        Assert.SkipUnless(Python() is not null, "Python 3 is not installed.");
        Scan(line).ShouldBe(1);
    }

    [Fact]
    public void ScannerPassesCleanText()
    {
        Assert.SkipUnless(Python() is not null, "Python 3 is not installed.");
        Scan("A normal line about C:\\Users\\X\\example and noreply@example.com.").ShouldBe(0);
    }

    private static int Scan(string text)
    {
        var info = new ProcessStartInfo(Python()!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-I");
        info.ArgumentList.Add(Path.Combine(RepositoryFiles.Root, "tools", "scan-secrets.py"));
        info.ArgumentList.Add("--stdin");
        using var process = Process.Start(info)!;
        process.StandardInput.Write(text);
        process.StandardInput.Close();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
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
