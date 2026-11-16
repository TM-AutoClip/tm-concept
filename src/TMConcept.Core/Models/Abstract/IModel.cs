namespace TMConcept.Core.Models;

/// <summary>
/// A model the app can use in a step, wherever it runs (0.1.17, the developer: many local and online models mixed freely in ONE pipeline).
/// Every catalog model has one (<see cref="IModelDirectory.Get"/>): <see cref="IOnlineModel"/> runs somewhere else (a provider, or a server
/// on another computer), <see cref="ILocalModel"/> on this computer, where the graphics card holds one local model at a time
/// (<see cref="IGpuCoordinator"/>). The step code keeps calling the model through its usual service (chat client, image engine, speech engine,
/// embedder); this object only says what the model is and how it is put on and taken off this computer.
/// </summary>
public interface IModel
{
    /// <summary>The catalog entry (prices, limits, what it needs).</summary>
    CatalogModel Catalog { get; }

    /// <summary>Provider code (<c>local</c>, <c>endpoint:&lt;id&gt;</c> or an online provider).</summary>
    string Provider { get; }

    /// <summary>The model id sent to the provider or engine.</summary>
    string Id { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>What it can do (chat, image, speech, embedding…).</summary>
    ModelCapability Capabilities { get; }

    /// <summary>Whether it runs on this computer (<see cref="ILocalModel"/>).</summary>
    bool IsLocal { get; }

    /// <summary>"provider/id": the same model has the same key (the coordinator compares models by it).</summary>
    string Key { get; }
}