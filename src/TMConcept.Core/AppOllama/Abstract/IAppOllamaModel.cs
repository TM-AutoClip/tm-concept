using TMConcept.Core.Models;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// A model of the app's own Ollama copy as a local model (<see cref="ILocalModel"/>): it takes the graphics card through
/// <see cref="IGpuCoordinator"/> like every other local model; loading starts the app's server when needed, unloading asks it to drop the
/// model (<c>keep_alive: 0</c>). Its memory need comes from its download size and the context it is run with.
/// </summary>
public interface IAppOllamaModel : ILocalModel
{
    /// <summary>Bytes of its weights (the download).</summary>
    long WeightBytes { get; }

    /// <summary>The context it is loaded with (<c>num_ctx</c>).</summary>
    long ContextTokens { get; }
}
