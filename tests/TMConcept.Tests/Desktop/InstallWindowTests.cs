using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Tests.Desktop;

/// <summary>The modal install window: one line per component, closes by itself when done, stays with the reason after a failure.</summary>
public sealed class InstallWindowTests
{
    private static DesktopHarness Harness(IInstallDialog? dialog, params FakeComponent[] components) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(components));
        if (dialog is not null)
        {
            s.AddSingleton(dialog);
        }
    });

    [AvaloniaFact]
    public async Task EachComponentHasItsLineAndAFailureIsShownWithItsReason()
    {
        var node = new FakeComponent(ComponentCodes.Node, ready: false);
        var engine = new FakeComponent("engine.fake", ready: false, ComponentCodes.Node) { Fail = true };
        var voice = new FakeComponent("voice.fake", ready: false);
        var dialog = new RecordingDialog();
        using var h = Harness(dialog, node, engine, voice);
        await FirstRunTests.CreateAsync(h);

        var outcome = await h.Get<ComponentInstallFlow>().EnsureAsync([engine.Code, voice.Code], "install.purpose.video", null, CancellationToken.None);

        outcome.Ready.ShouldBeFalse();
        dialog.Runs.ShouldBe(1, "one window for the whole plan");
        var model = dialog.Model!;
        model.Steps.Select(s => s.Code).ShouldBe([node.Code, engine.Code, voice.Code], "dependencies first, in install order");
        model.Steps.Select(s => s.State).ShouldBe([ComponentStepState.Done, ComponentStepState.Failed, ComponentStepState.Done]);
        model.Steps[1].Detail.ShouldBe("fake failure");
        model.Error.ShouldBe(outcome.Reason);
        model.CanClose.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task TheWindowClosesByItselfWhenEverythingIsInstalledAndStaysAfterAFailure()
    {
        using var h = Harness(null);
        await FirstRunTests.CreateAsync(h);
        var owner = new Window();
        owner.Show();
        h.Get<WindowHost>().Active = owner;
        var dialog = h.Get<IInstallDialog>();
        var text = h.Get<Localizer>();

        using (var done = new InstallViewModel([new FakeComponent("a", ready: false)], text))
        {
            (await dialog.RunAsync(done, m => Task.FromResult(string.Empty))).ShouldBeEmpty();
            done.Running.ShouldBeFalse();
        }

        using var failed = new InstallViewModel([new FakeComponent("b", ready: false)], text);
        var run = dialog.RunAsync(failed, m => Task.FromResult("install failed: disk full"));
        for (var i = 0; i < 50 && !failed.HasError; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        failed.Error.ShouldBe("install failed: disk full");
        run.IsCompleted.ShouldBeFalse("the window waits for the user after a failure");
        failed.CloseCommand.Execute(null);
        (await run).ShouldBe("install failed: disk full");
        owner.Close();
    }

    [Fact]
    public void AFinishedLineDoesNotGoBackToRunningWhenALateProgressArrives()
    {
        using var h = new DesktopHarness();
        using var model = new InstallViewModel([new FakeComponent("a", ready: false)], h.Get<Localizer>());

        model.Apply(new ComponentStep(0, "a", ComponentStepState.Running, 0.5, "12 / 24 MB"));
        model.Steps[0].Detail.ShouldBe("12 / 24 MB");
        model.Apply(new ComponentStep(0, "a", ComponentStepState.Done, 1, string.Empty));
        model.Apply(new ComponentStep(0, "a", ComponentStepState.Running, 0.9, "late"));

        model.Steps[0].State.ShouldBe(ComponentStepState.Done);
    }

    /// <summary>Runs the install without a window and keeps the model (the progress reports are let through before it returns).</summary>
    private sealed class RecordingDialog : IInstallDialog
    {
        public InstallViewModel? Model { get; private set; }

        public int Runs { get; private set; }

        public async Task<string> RunAsync(InstallViewModel model, Func<InstallViewModel, Task<string>> install)
        {
            Runs++;
            Model = model;
            var error = await install(model);
            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Yield();
            }

            model.Finish(error);
            return error;
        }
    }
}
