using System.Globalization;
using TMConcept.Core.Music;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <inheritdoc cref="IMusicProgressText" />
public sealed class MusicProgressText(Localizer text) : IMusicProgressText
{
    private const string Separator = " · ";

    /// <inheritdoc />
    public string Line(MusicProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var parts = new List<string>();
        if (progress.Staged)
        {
            parts.Add(text.Format("music.progress.stage", progress.Stage, progress.Stages));
        }

        if (progress.Counted && progress.Unit != MusicProgressUnit.None)
        {
            parts.Add(text.Format("music.progress." + Key(progress.Unit), Number(progress.Done), Number(progress.Total)));
            parts.Add(text.Format("video.percent", progress.Percent.ToString(CultureInfo.InvariantCulture)));
        }
        else if (progress.Unit == MusicProgressUnit.Decoding)
        {
            parts.Add(text["music.progress.decoding.running"]);
        }

        return string.Join(Separator, parts);
    }

    /// <inheritdoc />
    public string WithTime(MusicProgress progress, TimeSpan elapsed)
    {
        var line = Line(progress);
        var time = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
        return line.Length == 0 ? text.Format("music.progress.elapsed", time) : line + Separator + time;
    }

    /// <inheritdoc />
    public string Video(MusicProgress progress)
    {
        var line = Line(progress);
        return line.Length == 0 ? text["music.progress.making"] : text.Format("music.progress.video", line);
    }

    /// <summary>The unit's text key (every unit of <see cref="MusicProgressUnit"/> has one; a new unit without one fails its test).</summary>
    private static string Key(MusicProgressUnit unit) => unit switch
    {
        MusicProgressUnit.Tokens => "tokens",
        MusicProgressUnit.Blocks => "blocks",
        MusicProgressUnit.Steps => "steps",
        MusicProgressUnit.Decoding => "decoding",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "A music progress unit without a text."),
    };

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
