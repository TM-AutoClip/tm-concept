using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TMConcept.Core.DiskSpace;
using TMConcept.Desktop.DiskSpace;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The app's real permanent delete (it takes only paths inside the harness's own temporary folders) that records every path it was given,
/// and fails like a file in use for the paths <see cref="FailWhen"/> picks. <see cref="Register"/> puts it in place of the app's delete.
/// </summary>
internal sealed class RecordingDiscard : IFolderDiscard
{
    private IFolderDiscard? _inner;

    public List<string> Discarded { get; } = [];

    public Func<string, bool> FailWhen { get; set; } = _ => false;

    public async Task DiscardAsync(string path, CancellationToken cancellationToken)
    {
        if (FailWhen(path))
        {
            throw new IOException("The process cannot access the file because it is being used by another process.");
        }

        await Inner.DiscardAsync(path, cancellationToken);
        Discarded.Add(path);
    }

    public bool Owns(string path) => Inner.Owns(path);

    /// <summary>Puts this one in place of the app's delete.</summary>
    public void Register(IServiceCollection services)
    {
        services.RemoveAll<IFolderDiscard>();
        services.AddSingleton<IFolderDiscard>(sp =>
        {
            _inner = sp.GetRequiredService<PermanentDiscard>();
            return this;
        });
    }

    private IFolderDiscard Inner => _inner ?? throw new InvalidOperationException("Not registered.");
}
