using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Shouldly;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Views;
using TMConcept.Tests.Architecture;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The shared "?" help mark (0.1.23): a short tooltip on hover and focus, the whole text in a small panel on a click, Enter or Space, a
/// name for screen readers; every "?" of the views has a text in English and Turkish, and every page and window the developer named has one.
/// </summary>
public sealed partial class HelpTipTests
{
    private const string Text = "Writes the concepts. Pick a model first, e.g. a local one. Then press the button.";

    [Theory]
    [InlineData("One sentence only", "One sentence only")]
    [InlineData("First part. Second part.", "First part.")]
    [InlineData("Kaldırdığın her şey (ör. ffmpeg) gider. Sonra bak.", "Kaldırdığın her şey (ör. ffmpeg) gider.")]
    [InlineData("Since 0.1.17 it works. Then more.", "Since 0.1.17 it works.")]
    [InlineData("Ask first? \"Yes\" goes on.", "Ask first?")]
    [InlineData("  ", "")]
    public void TheTooltipIsTheFirstSentence(string text, string expected) => HelpTip.FirstSentence(text).ShouldBe(expected);

    [AvaloniaFact]
    public void HoverShowsAShortTipAndAClickOpensTheWholeTextWithAName()
    {
        var text = new Localizer();
        text.Use("tr");
        var (window, tip) = Show(text);
        try
        {
            ToolTip.GetTip(tip).ShouldBe("Writes the concepts.");
            AutomationProperties.GetName(tip).ShouldBe(text["help.name"]);
            AutomationProperties.GetHelpText(tip).ShouldBe(Text);
            tip.Focusable.ShouldBeTrue();
            tip.IsTabStop.ShouldBeTrue();
            tip.IsPanelOpen.ShouldBeFalse();

            var at = tip.TranslatePoint(new Point(tip.Bounds.Width / 2, tip.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            tip.IsPanelOpen.ShouldBeTrue("a click opens the whole explanation");
            tip.PanelText.ShouldBe(Text);
            ToolTip.GetIsOpen(tip).ShouldBeFalse("the panel replaces the tooltip");

            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            tip.IsPanelOpen.ShouldBeFalse("a second click closes it");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheKeyboardReachesItAndOpensAndClosesThePanel()
    {
        var (window, tip) = Show(new Localizer());
        try
        {
            tip.Focus(NavigationMethod.Tab).ShouldBeTrue();
            Dispatcher.UIThread.RunJobs();
            ToolTip.GetIsOpen(tip).ShouldBeTrue("the keyboard focus shows the tip at once");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            tip.IsPanelOpen.ShouldBeTrue();

            tip.ClosePanel();
            Dispatcher.UIThread.RunJobs();
            tip.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            tip.IsPanelOpen.ShouldBeTrue("Space opens it too");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnExplicitSummaryIsTheTooltipAndAnEmptyTextOpensNothing()
    {
        var (window, tip) = Show(new Localizer());
        try
        {
            tip.Summary = "Short.";
            ToolTip.GetTip(tip).ShouldBe("Short.");
            tip.Text = string.Empty;
            tip.OpenPanel();
            tip.IsPanelOpen.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void ItKeepsItsSizeInLightAndDark(string theme)
    {
        var (window, tip) = Show(new Localizer(), theme == "dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        try
        {
            tip.Bounds.Width.ShouldBeInRange(16, 24, "a small round sign next to a title, never wider in one theme");
            tip.Bounds.Height.ShouldBeInRange(16, 24);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void EveryHelpMarkOfTheViewsHasATextInEnglishAndTurkish()
    {
        var en = Localizer.Keys("en").ToHashSet(StringComparer.Ordinal);
        var tr = Localizer.Keys("tr").ToHashSet(StringComparer.Ordinal);
        var marks = Views().SelectMany(f => HelpMark().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Mark: m.Value))).ToList();
        marks.Count.ShouldBeGreaterThan(60);
        foreach (var (file, mark) in marks)
        {
            var key = ResourceKey().Match(mark);
            if (key.Success)
            {
                (en.Contains(key.Groups[1].Value) && tr.Contains(key.Groups[1].Value)).ShouldBeTrue($"{file}: {key.Groups[1].Value}");
            }
            else
            {
                mark.ShouldContain("Text=\"{Binding ", Case.Sensitive, $"{file}: a \"?\" without a text");
            }
        }

        var help = en.Where(k => k.StartsWith("help.", StringComparison.Ordinal)).ToList();
        help.Count.ShouldBeGreaterThan(25);
        foreach (var language in new[] { "en", "tr" })
        {
            var text = new Localizer();
            text.Use(language);
            foreach (var key in help.Where(k => k != "help.name"))
            {
                var sentences = Sentence().Matches(text[key].Trim()).Count;
                sentences.ShouldBeInRange(2, 5, $"{language} {key}: two to four short sentences (a sentence may carry a short example)");
            }
        }
    }

    [Theory]
    [InlineData("ConceptsView.axaml")]
    [InlineData("TopicSectionView.axaml")]
    [InlineData("IdeaSectionView.axaml")]
    [InlineData("DocumentSectionView.axaml")]
    [InlineData("ResearchSectionView.axaml")]
    [InlineData("VideoView.axaml")]
    [InlineData("VideoStylesSettingsView.axaml")]
    [InlineData("GeneralSettingsView.axaml")]
    [InlineData("KeysSettingsView.axaml")]
    [InlineData("LocalAiSettingsView.axaml")]
    [InlineData("VoiceSettingsView.axaml")]
    [InlineData("ResearchSettingsView.axaml")]
    [InlineData("StorageSettingsView.axaml")]
    [InlineData("BackupSettingsView.axaml")]
    [InlineData("AboutSettingsView.axaml")]
    [InlineData("ProfileView.axaml")]
    [InlineData("LogWindow.axaml")]
    [InlineData("InstallWindow.axaml")]
    [InlineData("AppUpdateWindow.axaml")]
    [InlineData("DocumentLibraryView.axaml")]
    [InlineData("StepPickerView.axaml")]
    public void EveryPageAndWindowHasHelpMarks(string view) =>
        HelpMark().IsMatch(File.ReadAllText(Path.Combine(RepositoryFiles.Root, "src", "TMConcept.Desktop", "Views", view))).ShouldBeTrue(view);

    private static IEnumerable<string> Views() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryFiles.Root, "src", "TMConcept.Desktop", "Views"), "*.axaml");

    private static (Window Window, HelpTip Tip) Show(Localizer text, Avalonia.Styling.ThemeVariant? theme = null)
    {
        var tip = new HelpTip { Text = Text };
        var window = new Window { Content = new StackPanel { Children = { new TextBlock { Text = "Title" }, tip } }, Width = 400, Height = 200 };
        if (theme is not null)
        {
            window.RequestedThemeVariant = theme;
        }

        new LanguageResources(text).Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, tip);
    }

    [GeneratedRegex(@"<(?:views|v):HelpTip\b[^>]*>")]
    private static partial Regex HelpMark();

    [GeneratedRegex(@"Text=""\{DynamicResource ([\w.\-]+)\}""")]
    private static partial Regex ResourceKey();

    [GeneratedRegex(@"[^.!?…]+[.!?…]+(?=\s+[\p{Lu}""“(]|\s*$)")]
    private static partial Regex Sentence();
}
