using Avalonia.Controls;
using Avalonia.Threading;

namespace TMConcept.Desktop.Localization;

/// <summary>
/// Writes the texts of the current language into the application's resources: views read a text with <c>{DynamicResource key}</c>; when the
/// language changes the dictionary is replaced in one step and every text on screen changes without a restart.
/// </summary>
public sealed class LanguageResources
{
    private readonly Localizer _localizer;
    private IList<IResourceProvider>? _target;
    private ResourceDictionary _current;

    /// <summary>Builds the dictionary of the current language and follows language changes.</summary>
    public LanguageResources(Localizer localizer)
    {
        _localizer = localizer;
        _current = Build();
        _localizer.LanguageChanged += (_, _) =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Replace();
            }
            else
            {
                Dispatcher.UIThread.Post(Replace);
            }
        };
    }

    /// <summary>Adds the dictionary to the given merged dictionaries (the application's resources).</summary>
    public void Attach(IList<IResourceProvider> mergedDictionaries)
    {
        ArgumentNullException.ThrowIfNull(mergedDictionaries);
        _target = mergedDictionaries;
        mergedDictionaries.Add(_current);
    }

    private void Replace()
    {
        var next = Build();
        if (_target is not null)
        {
            var index = _target.IndexOf(_current);
            if (index >= 0)
            {
                _target[index] = next;
            }
            else
            {
                _target.Add(next);
            }
        }

        _current = next;
    }

    private ResourceDictionary Build()
    {
        var dictionary = new ResourceDictionary();
        foreach (var (key, text) in _localizer.Strings)
        {
            dictionary.Add(key, text);
        }

        return dictionary;
    }
}
