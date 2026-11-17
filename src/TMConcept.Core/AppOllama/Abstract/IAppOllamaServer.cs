namespace TMConcept.Core.AppOllama;

/// <summary>
/// The app's own Ollama server: started on demand as a child of the app (never at app start, never the user's Ollama), on 127.0.0.1 and a port
/// of its own, with the app's own models folder; stopped when the app closes (and when the storage manager removes its files). Starting never
/// downloads anything: the program must have been installed after the user confirmed it.
/// </summary>
public interface IAppOllamaServer
{
    /// <summary>Whether the server runs now (started by this app and not ended).</summary>
    bool Running { get; }

    /// <summary>The root address of the running server (<c>http://127.0.0.1:11534/</c>); null when it does not run.</summary>
    Uri? Address { get; }

    /// <summary>
    /// The address of the running server: starts it first when it does not run and waits until it answers. Throws
    /// <see cref="AppOllamaException"/> when the program is not installed or the server did not come up.
    /// </summary>
    Task<Uri> StartAsync(CancellationToken cancellationToken);

    /// <summary>Ends the server and its model runners (nothing when it does not run).</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
