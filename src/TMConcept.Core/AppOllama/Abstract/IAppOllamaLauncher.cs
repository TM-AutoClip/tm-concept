namespace TMConcept.Core.AppOllama;

/// <summary>Starts the app's own Ollama server process (tied to the app, so it never outlives it) and checks ports.</summary>
public interface IAppOllamaLauncher
{
    /// <summary>Whether <paramref name="port"/> on 127.0.0.1 can be listened on now.</summary>
    bool IsPortFree(int port);

    /// <summary>Any free port the system gives on 127.0.0.1.</summary>
    int AnyFreePort();

    /// <summary>Starts <c>ollama serve</c> as <paramref name="launch"/> says; throws when the program cannot be started.</summary>
    IAppOllamaProcess Start(AppOllamaLaunch launch);
}
