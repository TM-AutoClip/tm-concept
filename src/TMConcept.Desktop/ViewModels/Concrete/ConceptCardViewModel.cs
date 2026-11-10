using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Concepts;
using TMConcept.Core.Scenes;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A concept card: title and summary; the full text is loaded when the card is opened.</summary>
public sealed partial class ConceptCardViewModel(Concept concept, Localizer text) : ObservableObject
{
    /// <summary>The concept (without its text until opened).</summary>
    public Concept Concept { get; } = concept;

    /// <summary>Id.</summary>
    public Guid Id => Concept.Id;

    /// <summary>Title with its number.</summary>
    public string Title => string.Create(CultureInfo.InvariantCulture, $"#{Concept.Number} ") + Concept.Title;

    /// <summary>Summary.</summary>
    public string Summary => Concept.Summary;

    /// <summary>Duplicate note ("71% similar to #212 …") or empty.</summary>
    public string Note => Notes(Concept.DuplicateNote, text);

    /// <summary>Whether there is a note.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>Deleted at (trash).</summary>
    public string DeletedAt => Concept.DecidedAtUtc is { } at ? text.Format("trash.deleted_at", at.ToLocalTime().ToString("d", text.Culture)) : string.Empty;

    /// <summary>Lines of the full text.</summary>
    public ObservableCollection<ContentLine> Lines { get; } = [];

    /// <summary>Whether the card is open.</summary>
    [ObservableProperty]
    public partial bool Expanded { get; set; }

    /// <summary>Checked (trash selection).</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>Shows a new title and summary (an edit was saved).</summary>
    public void Retitle(string title, string summary)
    {
        Concept.Title = title ?? string.Empty;
        Concept.Summary = summary ?? string.Empty;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Shows the full text.</summary>
    public void ShowContent(string content)
    {
        Lines.Clear();
        foreach (var line in (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                Lines.Add(new ContentLine(line.Trim(), SceneSplitter.IsSceneLine(line)));
            }
        }
    }

    /// <summary>The text of a duplicate code ("similar:212:0.71", "continuation:12").</summary>
    public static string Notes(string code, Localizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = (code ?? string.Empty).Split(':');
        return parts[0] switch
        {
            "similar" when parts.Length == 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var similarity) =>
                "⚠ " + text.Format("concepts.similar", Math.Round(similarity * 100), parts[1]),
            "continuation" when parts.Length >= 2 => "↪ " + text.Format("concepts.continuation", parts[1]),
            _ => string.Empty,
        };
    }
}
