using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The detailed log page (its own window, opened from the activity panel's "Show all"): every entry of the kept log files (the app's and the
/// speech worker's, <see cref="LogRules.KeepDays"/> days), one day at a time, filtered by level (all, warnings and errors, errors), by text
/// (message, category, the exception below it) and to this session; Copy puts the shown entries on the clipboard as they stand in the file,
/// Open folder opens the logs folder. Today's files are followed live (<see cref="TailAsync"/>, about every second while the window is
/// open): new entries appear at the bottom. At most <see cref="MaxShown"/> entries are shown (the newest).
/// </summary>
public sealed partial class LogViewerViewModel : ObservableObject
{
    /// <summary>The most entries shown at once (the newest).</summary>
    public const int MaxShown = 5000;

    private readonly LogFileReader _reader;
    private readonly AppPaths _paths;
    private readonly IShellLauncher _shell;
    private readonly Localizer _text;
    private readonly TimeProvider _clock;
    private readonly ILogger<LogViewerViewModel> _logger;
    private readonly List<LogEntry> _all = [];
    private readonly Dictionary<string, long> _read = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _sessionStart;
    private bool _loading;
    private int _shown;

    /// <summary>Creates.</summary>
    public LogViewerViewModel(AppPaths paths, IShellLauncher shell, Localizer text, TimeProvider clock, ILogger<LogViewerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(text);
        _paths = paths;
        _shell = shell;
        _text = text;
        _clock = clock;
        _logger = logger;
        _reader = new LogFileReader(paths.LogDirectory);
        using var process = Process.GetCurrentProcess();
        _sessionStart = new DateTimeOffset(process.StartTime).AddSeconds(-1);
        Levels =
        [
            new ChoiceOption("all", text["logs.level.all"]),
            new ChoiceOption("warning", text["logs.level.warning"]),
            new ChoiceOption("error", text["logs.level.error"]),
        ];
        _loading = true;
        Level = Levels[0];
        _loading = false;
    }

    /// <summary>The days with a log file, newest first (<c>2026-11-16</c>).</summary>
    public ObservableCollection<string> Days { get; } = [];

    /// <summary>The level choices.</summary>
    public IReadOnlyList<ChoiceOption> Levels { get; }

    /// <summary>The shown entries, oldest first.</summary>
    public ObservableCollection<LogEntry> Lines { get; } = [];

    /// <summary>The chosen day.</summary>
    [ObservableProperty]
    public partial string? Day { get; set; }

    /// <summary>The chosen level.</summary>
    [ObservableProperty]
    public partial ChoiceOption? Level { get; set; }

    /// <summary>The search text (empty = everything).</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>Only the entries since this app started.</summary>
    [ObservableProperty]
    public partial bool SessionOnly { get; set; }

    /// <summary>"124 of 3 210 entries".</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>Whether the chosen day is today (its files are followed live).</summary>
    public bool Live => Day == Today();

    /// <summary>Reads the days and the newest day.</summary>
    public void Load()
    {
        _loading = true;
        Days.Clear();
        foreach (var day in _reader.Days())
        {
            Days.Add(Format(day));
        }

        var today = Today();
        if (!Days.Contains(today))
        {
            Days.Insert(0, today);
        }

        Day = Day is { } chosen && Days.Contains(chosen) ? chosen : Days[0];
        _loading = false;
        ReadDay();
    }

    /// <summary>Reads the chosen day again.</summary>
    [RelayCommand]
    public void Refresh() => Load();

    /// <summary>Adds what was written to today's files since the last read (called about every second while the window is open).</summary>
    public Task TailAsync()
    {
        if (!Live || !DateOnly.TryParseExact(Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return Task.CompletedTask;
        }

        var added = new List<LogEntry>();
        foreach (var (file, source) in _reader.Files(day))
        {
            var (entries, next) = LogFileReader.ReadFrom(file, source, _read.GetValueOrDefault(file));
            _read[file] = next;
            added.AddRange(entries);
        }

        if (added.Count > 0)
        {
            // Appended, not rebuilt: the window keeps its scroll position and stays light while new lines come in.
            foreach (var entry in added.OrderBy(e => e.At))
            {
                _all.Add(entry);
                if (Passes(entry))
                {
                    _shown++;
                    Lines.Add(entry);
                }
            }

            while (Lines.Count > MaxShown)
            {
                Lines.RemoveAt(0);
            }

            ShowSummary();
        }

        return Task.CompletedTask;
    }

    /// <summary>The shown entries as they stand in the files (for the clipboard).</summary>
    public string CopyText() => string.Join(Environment.NewLine, Lines.Select(l => l.Text));

    /// <summary>Opens the logs folder.</summary>
    [RelayCommand]
    public void OpenFolder()
    {
        try
        {
            _shell.OpenPath(_paths.LogDirectory);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(ex, "The logs folder could not be opened");
        }
    }

    partial void OnDayChanged(string? value)
    {
        OnPropertyChanged(nameof(Live));
        if (!_loading)
        {
            ReadDay();
        }
    }

    partial void OnLevelChanged(ChoiceOption? value)
    {
        if (!_loading)
        {
            Apply();
        }
    }

    partial void OnSearchChanged(string value) => Apply();

    partial void OnSessionOnlyChanged(bool value) => Apply();

    private void ReadDay()
    {
        _all.Clear();
        _read.Clear();
        if (DateOnly.TryParseExact(Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            var (entries, read) = _reader.ReadDay(day);
            _all.AddRange(entries);
            foreach (var (file, length) in read)
            {
                _read[file] = length;
            }
        }

        Apply();
    }

    /// <summary>Shows the entries that pass the filters (the newest <see cref="MaxShown"/>).</summary>
    private void Apply()
    {
        var shown = _all.Where(Passes).ToList();
        _shown = shown.Count;
        Lines.Clear();
        foreach (var entry in shown.Skip(Math.Max(0, shown.Count - MaxShown)))
        {
            Lines.Add(entry);
        }

        ShowSummary();
    }

    /// <summary>Whether <paramref name="entry"/> passes the level, session and text filters.</summary>
    private bool Passes(LogEntry entry)
    {
        var minimum = Level?.Value switch
        {
            "warning" => 3,
            "error" => 4,
            _ => 0,
        };
        var search = Search.Trim();
        return entry.Rank >= minimum && (!SessionOnly || entry.At >= _sessionStart)
            && (search.Length == 0 || entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase) || entry.Detail.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowSummary() => Summary = _shown > MaxShown
        ? _text.Format("logs.summary.cut", MaxShown, _shown, _all.Count)
        : _text.Format("logs.summary", _shown, _all.Count);

    private string Today() => Format(DateOnly.FromDateTime(_clock.GetLocalNow().DateTime));

    private static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
