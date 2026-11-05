using Microsoft.Extensions.DependencyInjection;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Operating system specifics (key protector, folders, audio output, tool archives). The modules come from a list and the current one
/// registers itself: no "if Windows" branch in the rest of the code.
/// </summary>
public interface IOsModule
{
    /// <summary>Whether this module is for the running operating system.</summary>
    bool IsCurrent { get; }

    /// <summary>Default data folder.</summary>
    string DefaultDataDirectory();

    /// <summary>The user's documents folder.</summary>
    string DocumentsDirectory();

    /// <summary>Registers the services.</summary>
    void Register(IServiceCollection services);
}
