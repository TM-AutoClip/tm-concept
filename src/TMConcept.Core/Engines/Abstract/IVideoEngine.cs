using TMConcept.Core.Tools;

namespace TMConcept.Core.Engines;

/// <summary>
/// A video engine: its own skeleton (the app's files) + the model's single code file, a local render with no GPU and no network, and an
/// install that runs only after the user agreed. Engines come from DI; the code never branches on the engine kind.
/// </summary>
public interface IVideoEngine
{
    /// <summary>Kind.</summary>
    EngineKind Kind { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>Shown as experimental.</summary>
    bool Experimental { get; }

    /// <summary>The user must accept a license before the install (Remotion).</summary>
    bool NeedsLicenseConsent { get; }

    /// <summary>The model's file inside the work folder.</summary>
    string CodeFile { get; }

    /// <summary>The contract the code model follows (English; part of the video code prompt).</summary>
    string Contract { get; }

    /// <summary>Hand-written sample code (install check without a model).</summary>
    string SampleCode { get; }

    /// <summary>Install state (files only; starts no process, uses no network).</summary>
    EngineStatus Status();

    /// <summary>Installs packages and the browser. Network; only after the user agreed.</summary>
    Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Checks the code and writes skeleton + code + input into the work folder. A failed check writes nothing.</summary>
    void WriteProject(EngineRenderRequest request);

    /// <summary>Renders the silent video (after <see cref="WriteProject"/>).</summary>
    Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken);
}
