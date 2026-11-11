using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Desktop.Controls;

namespace TMConcept.Tests.Desktop;

public sealed class AdaptiveSplitPanelTests
{
    private static (AdaptiveSplitPanel Panel, Border First, Border Second) Layout(double width, double height)
    {
        var first = new Border();
        var second = new Border { MinHeight = 100 };
        var panel = new AdaptiveSplitPanel { Breakpoint = 1200, Ratio = 0.53, Spacing = 12 };
        panel.Children.Add(first);
        panel.Children.Add(second);
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
        return (panel, first, second);
    }

    [AvaloniaFact]
    public void AtOrAboveTheBreakpointTheChildrenStandSideBySideAtFullHeight()
    {
        var (_, first, second) = Layout(1300, 600);
        var free = 1300 - 12.0;
        // Layout rounding may move an edge by less than a pixel.
        (first.Bounds.X, first.Bounds.Y, first.Bounds.Height).ShouldBe((0, 0, 600));
        first.Bounds.Width.ShouldBe(free * 0.53, 1);
        second.Bounds.X.ShouldBe((free * 0.53) + 12, 1);
        second.Bounds.Right.ShouldBe(1300, 1);
        (second.Bounds.Y, second.Bounds.Height).ShouldBe((0, 600));
    }

    [AvaloniaFact]
    public void BelowTheBreakpointTheyStackWithHalfTheHeightEachButNotBelowTheirMinimum()
    {
        var (_, first, second) = Layout(1100, 600);
        first.Bounds.ShouldBe(new Rect(0, 0, 1100, 294));
        second.Bounds.ShouldBe(new Rect(0, 306, 1100, 294));

        var (_, top, bottom) = Layout(1100, 150);
        top.Bounds.Height.ShouldBe(69);
        bottom.Bounds.Height.ShouldBe(100, "a child's own minimum height wins over half");
    }

    [AvaloniaFact]
    public void FitFirstGivesTheFirstChildOnlyTheHeightItNeedsUpToItsShare()
    {
        static (Border First, Border Second) Fit(double wanted)
        {
            var first = new Border { Height = wanted };
            var second = new Border();
            var panel = new AdaptiveSplitPanel { Breakpoint = 1200, StackedRatio = 0.4, FitFirst = true, Spacing = 10 };
            panel.Children.Add(first);
            panel.Children.Add(second);
            panel.Measure(new Size(800, 610));
            panel.Arrange(new Rect(0, 0, 800, 610));
            return (first, second);
        }

        var (shortFirst, rest) = Fit(50);
        shortFirst.Bounds.Height.ShouldBe(50);
        rest.Bounds.ShouldBe(new Rect(0, 60, 800, 550), "the second child gets the rest");
        Fit(500).Second.Bounds.Y.ShouldBe(250, "never more than its share");
    }

    [AvaloniaFact]
    public void BelowWideBelowHeightTheyStandSideBySideWhateverTheWidth()
    {
        var panel = new AdaptiveSplitPanel { Breakpoint = 1200, WideBelowHeight = 380 };
        panel.IsWideAt(new Size(700, 300)).ShouldBeTrue("too low to stack");
        panel.IsWideAt(new Size(700, 600)).ShouldBeFalse();
        panel.IsWideAt(1300).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void AnyOtherNumberOfChildrenThrows()
    {
        var panel = new AdaptiveSplitPanel();
        panel.Children.Add(new Border());
        panel.Children.Add(new Border());
        panel.Children.Add(new Border());
        Should.Throw<InvalidOperationException>(() => panel.Measure(new Size(1300, 600)));
    }
}
