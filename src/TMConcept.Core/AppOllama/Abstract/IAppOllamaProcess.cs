namespace TMConcept.Core.AppOllama;

/// <summary>A running <c>ollama serve</c> of the app (<see cref="IAppOllamaLauncher.Start"/>). Disposing it ends it.</summary>
public interface IAppOllamaProcess : IDisposable
{
    /// <summary>Whether the process has ended.</summary>
    bool HasExited { get; }

    /// <summary>Its exit code once it ended (null while it runs or when unknown).</summary>
    int? ExitCode { get; }

    /// <summary>Its last output lines (for the error when it ended at start; never logged in full).</summary>
    string RecentOutput { get; }

    /// <summary>Ends it with its children (the model runners); nothing when it already ended.</summary>
    void Stop();
}
