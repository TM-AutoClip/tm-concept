using System.Reflection;
using System.Runtime.InteropServices;

namespace TMConcept.Desktop.Logging;

/// <summary>What a log header, a problem report and an issue link say about the running program (nothing personal).</summary>
public static class AppIdentity
{
    /// <summary>Product name.</summary>
    public static string Name => "TM Concept";

    /// <summary>Version with the commit it was built from (<c>0.1.4+&lt;commit&gt;</c>).</summary>
    public static string Version { get; } =
        typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Version without the commit (<c>0.1.4</c>).</summary>
    public static string ShortVersion => Version.Split('+')[0];

    /// <summary>The operating system and its version.</summary>
    public static string Os => RuntimeInformation.OSDescription.Trim();

    /// <summary>The operating system's architecture and the process's (<c>X64</c>, or <c>Arm64 (process X64)</c> under emulation).</summary>
    public static string Architecture => RuntimeInformation.OSArchitecture == RuntimeInformation.ProcessArchitecture
        ? RuntimeInformation.OSArchitecture.ToString()
        : RuntimeInformation.OSArchitecture + " (process " + RuntimeInformation.ProcessArchitecture + ")";

    /// <summary>The .NET runtime.</summary>
    public static string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>The first line of a log file and of every start: <c>=== TM Concept 0.1.4+… · app · Windows … · X64 · .NET 10.0.x ===</c>.</summary>
    public static string Header(string kind) =>
        "=== " + Name + " " + Version + " · " + kind + " · " + Os + " · " + Architecture + " · " + Runtime + " · process " + Environment.ProcessId + " ===";
}
