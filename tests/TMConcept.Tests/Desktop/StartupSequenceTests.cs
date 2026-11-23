using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.38: the start runs its steps in order off the interface thread and names each on the splash; a failure stops it (later steps, the
/// main window among them, do not run) and the splash shows why; a failure its step takes over (the unlock window) shows nothing; Cancel
/// ends the child processes, then frees the lock, then ends the process.
/// </summary>
public sealed class StartupSequenceTests
{
    private sealed class FakeScreen : IStartupScreen
    {
        private readonly Lock _gate = new();

        public List<StartupStage> Stages { get; } = [];

        public List<(StartupStage Stage, string Message)> Errors { get; } = [];

        public void Report(StartupStage value)
        {
            lock (_gate)
            {
                Stages.Add(value);
            }
        }

        public void ShowError(StartupStage stage, string message) => Errors.Add((stage, message));
    }

    private sealed class FakeExit : IStartupExit
    {
        public List<string> Calls { get; } = [];

        public void KillChildren() => Calls.Add("kill");

        public void ReleaseLock() => Calls.Add("lock");

        public void End(int exitCode) => Calls.Add("end " + exitCode);
    }

    private static StartupStep Step(StartupStage stage, List<string> ran, Exception? fail = null, Func<Exception, bool>? handle = null) =>
        new(stage, async _ =>
        {
            await Task.Yield();
            lock (ran)
            {
                ran.Add(stage.ToString());
            }

            if (fail is not null)
            {
                throw fail;
            }
        }, handle);

    [Fact]
    public async Task TheStepsRunInOrderAndEachIsNamedOnTheScreen()
    {
        var screen = new FakeScreen();
        var ran = new List<string>();
        var sequence = new StartupSequence(new FakeExit());

        var outcome = await sequence.RunAsync(
            [Step(StartupStage.Preparing, ran), Step(StartupStage.Components, ran), Step(StartupStage.Vault, ran), Step(StartupStage.Session, ran)], screen);

        outcome.ShouldBe(StartupOutcome.Completed);
        ran.ShouldBe(["Preparing", "Components", "Vault", "Session"]);
        screen.Stages.ShouldBe([StartupStage.Preparing, StartupStage.Components, StartupStage.Vault, StartupStage.Session]);
        screen.Errors.ShouldBeEmpty();
        sequence.Failed.ShouldBeFalse();
    }

    [Fact]
    public async Task AStepRunsOffTheCallersThreadAndMayNameALaterStage()
    {
        var screen = new FakeScreen();
        var caller = Environment.CurrentManagedThreadId;
        var stepThread = caller;
        var sequence = new StartupSequence(new FakeExit());

        var outcome = await sequence.RunAsync(
        [
            new StartupStep(StartupStage.Vault, progress =>
            {
                stepThread = Environment.CurrentManagedThreadId;
                progress.Report(StartupStage.Database);
                return Task.CompletedTask;
            }),
        ], screen);

        outcome.ShouldBe(StartupOutcome.Completed);
        stepThread.ShouldNotBe(caller);
        screen.Stages.ShouldBe([StartupStage.Vault, StartupStage.Database]);
    }

    [Fact]
    public async Task AFailureStopsTheStartShowsWhyAndNothingAfterItRuns()
    {
        var screen = new FakeScreen();
        var ran = new List<string>();
        var mainWindowOpened = false;
        var sequence = new StartupSequence(new FakeExit());

        var outcome = await sequence.RunAsync(
            [Step(StartupStage.Preparing, ran), Step(StartupStage.Components, ran, new IOException("disk full")), Step(StartupStage.Session, ran)], screen);
        if (outcome == StartupOutcome.Completed)
        {
            mainWindowOpened = true;
        }

        outcome.ShouldBe(StartupOutcome.Failed);
        ran.ShouldBe(["Preparing", "Components"]);
        screen.Errors.ShouldBe([(StartupStage.Components, "disk full")]);
        mainWindowOpened.ShouldBeFalse();
        sequence.Failed.ShouldBeTrue();
    }

    [Fact]
    public async Task AFailureTheStepTakesOverShowsNoErrorAndStopsTheStart()
    {
        var screen = new FakeScreen();
        var ran = new List<string>();
        Exception? handed = null;
        var failure = new InvalidOperationException("keyring unavailable");
        var sequence = new StartupSequence(new FakeExit());

        var outcome = await sequence.RunAsync(
        [
            Step(StartupStage.Vault, ran, failure, ex =>
            {
                handed = ex;
                return true;
            }),
            Step(StartupStage.Session, ran),
        ], screen);

        outcome.ShouldBe(StartupOutcome.Redirected);
        handed.ShouldBeSameAs(failure);
        ran.ShouldBe(["Vault"]);
        screen.Errors.ShouldBeEmpty();
        sequence.Failed.ShouldBeFalse();
    }

    [Fact]
    public void CancelEndsTheChildrenThenFreesTheLockThenEndsTheProcessOnce()
    {
        var exit = new FakeExit();
        var sequence = new StartupSequence(exit);

        sequence.Cancel();
        sequence.Cancel();

        exit.Calls.ShouldBe(["kill", "lock", "end 0"]);
    }

    [Fact]
    public async Task CloseAfterAFailureEndsWithAnErrorCodeAndACanceledStartRunsNoMoreSteps()
    {
        var exit = new FakeExit();
        var sequence = new StartupSequence(exit);
        await sequence.RunAsync([Step(StartupStage.Preparing, [], new IOException("x"))], new FakeScreen());

        sequence.Cancel();

        exit.Calls.ShouldBe(["kill", "lock", "end 1"]);
        var ran = new List<string>();
        (await sequence.RunAsync([Step(StartupStage.Session, ran)], new FakeScreen())).ShouldBe(StartupOutcome.Canceled);
        ran.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void TheSplashNamesTheStageAndAfterAFailureShowsWhyWithClose()
    {
        var text = new Localizer();
        text.Use("tr");
        var exit = new FakeExit();
        var model = new SplashViewModel(text, new StartupSequence(exit));
        var window = new SplashWindow { DataContext = model };
        window.Show();
        try
        {
            model.Title.ShouldBe(text["splash.title"]);
            model.Report(StartupStage.Database);
            model.StageText.ShouldBe(text["splash.stage.database"]);
            model.StageNumber.ShouldBe(4);
            model.HasError.ShouldBeFalse();

            model.ShowError(StartupStage.Database, "bozuk dosya");

            model.HasError.ShouldBeTrue();
            model.Error.ShouldContain(text["splash.stage.database"]);
            model.Error.ShouldContain("bozuk dosya");
            model.CancelText.ShouldBe(text["common.close"]);
            // A late report does not hide the error.
            model.Report(StartupStage.Session);
            model.StageText.ShouldBe(text["splash.stage.database"]);

            model.CancelCommand.Execute(null);
            exit.Calls.ShouldBe(["kill", "lock", "end 0"]);
        }
        finally
        {
            window.Finish();
        }
    }
}
