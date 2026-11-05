using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Keys;

namespace TMConcept.Tests.Desktop;

public sealed class FirstRunTests
{
    internal const string Master = "a long master password";
    internal const string BackupPassword = "a long backup password";

    internal static async Task<FirstRunViewModel> CreateAsync(DesktopHarness h)
    {
        var model = h.Get<FirstRunViewModel>();
        model.Step = FirstRunStep.New;
        model.UseMasterPassword = true;
        model.MasterPassword = model.MasterPasswordRepeat = Master;
        model.BackupPassword = model.BackupPasswordRepeat = BackupPassword;
        await model.CreateAsync();
        return model;
    }

    [AvaloniaFact]
    public async Task NewStartNeedsABackupPasswordAndCreatesTheDatabase()
    {
        using var h = new DesktopHarness();
        var model = h.Get<FirstRunViewModel>();
        model.Step = FirstRunStep.New;
        var window = new FirstRunWindow { DataContext = model };
        window.Show();
        var completed = false;
        model.Completed += (_, _) => completed = true;

        model.UseMasterPassword = true;
        model.MasterPassword = model.MasterPasswordRepeat = Master;
        await model.CreateAsync();
        completed.ShouldBeFalse();
        model.Message.ShouldNotBeEmpty();
        h.Get<Vault>().State.ShouldBe(VaultState.NotCreated);

        model.BackupPassword = model.BackupPasswordRepeat = BackupPassword;
        await model.CreateAsync();
        completed.ShouldBeTrue(model.Message);
        h.Get<Vault>().State.ShouldBe(VaultState.Open);
        h.Get<Vault>().Method.ShouldBe(KeyMethods.Password);
        (await h.Get<BackupService>().HasPasswordAsync(CancellationToken.None)).ShouldBeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public async Task RestoreOnAnotherComputerBringsTheDataBack()
    {
        using var source = new DesktopHarness();
        await CreateAsync(source);
        await source.Get<TMConcept.Core.Profiles.IProfileStore>().SaveAsync(new TMConcept.Core.Profiles.Profile { Name = "Restored" }, CancellationToken.None);
        var file = await source.Get<BackupService>().CreateInFolderAsync(CancellationToken.None);

        using var target = new DesktopHarness();
        var model = target.Get<FirstRunViewModel>();
        model.Step = FirstRunStep.Restore;
        model.UseMasterPassword = true;
        model.MasterPassword = model.MasterPasswordRepeat = Master;
        model.BackupFile = file;
        model.BackupPassword = "wrong password here";
        await model.RestoreAsync();
        model.Message.ShouldNotBeEmpty();

        model.BackupPassword = BackupPassword;
        await model.RestoreAsync();
        target.Get<Vault>().State.ShouldBe(VaultState.Open);
        target.Get<AppSession>().Profile!.Name.ShouldBe("Restored");
        target.Get<AppSession>().PendingMessage.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void LanguageButtonsSwitchAtOnce()
    {
        using var h = new DesktopHarness();
        var model = h.Get<FirstRunViewModel>();
        model.ChooseTurkishCommand.Execute(null);
        h.Get<TMConcept.Desktop.Localization.Localizer>().Language.ShouldBe("tr");
        model.Step.ShouldBe(FirstRunStep.Choice);
        model.ChooseEnglishCommand.Execute(null);
        h.Get<TMConcept.Desktop.Localization.Localizer>()["common.back"].ShouldBe("Back");
    }
}
