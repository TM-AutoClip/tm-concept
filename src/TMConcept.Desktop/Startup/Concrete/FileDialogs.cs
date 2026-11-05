using Avalonia.Platform.Storage;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.Startup;

/// <summary>Open / save file pickers of the active window.</summary>
public sealed class FileDialogs(WindowHost host, Localizer text)
{
    /// <summary>Asks for a file to open; <c>null</c> when canceled.</summary>
    public async Task<string?> OpenAsync(string titleKey, string filterKey, IReadOnlyList<string> patterns)
    {
        var all = await OpenManyAsync(titleKey, filterKey, patterns, multiple: false).ConfigureAwait(true);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>Asks for files to open.</summary>
    public async Task<IReadOnlyList<string>> OpenManyAsync(string titleKey, string filterKey, IReadOnlyList<string> patterns, bool multiple)
    {
        if (host.Active is not { } window)
        {
            return [];
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = text[titleKey],
            AllowMultiple = multiple,
            FileTypeFilter = [new FilePickerFileType(text[filterKey]) { Patterns = [.. patterns] }],
        }).ConfigureAwait(true);
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }
}
