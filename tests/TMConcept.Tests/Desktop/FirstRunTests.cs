using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
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
        var file = await source.Get<BackupService>().CreateInFolderAsync(null, CancellationToken.None);

        var confirm = new FakeConfirm();
        using var target = new DesktopHarness(s => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<TMConcept.Desktop.Dialogs.IConfirmDialog>(s, confirm));
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
        confirm.Messages.Single().ShouldContain("1 ");
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

    [AvaloniaFact]
    public void BackClearsTheBackupFieldsTheTwoPagesShare()
    {
        using var h = new DesktopHarness();
        var model = h.Get<FirstRunViewModel>();
        model.ChooseRestoreCommand.Execute(null);
        model.BackupFile = "C:/somewhere/old.tmcbak";
        model.BackupPassword = BackupPassword;

        model.BackCommand.Execute(null);
        model.ChooseNewCommand.Execute(null);

        model.BackupPassword.ShouldBeEmpty("the password of the backup to restore is not the new backup password");
        model.BackupPasswordRepeat.ShouldBeEmpty();
        model.BackupFile.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ARefusedBackupIsSaidPlainlyWithoutTheTechnicalReason()
    {
        using var source = new DesktopHarness();
        await CreateAsync(source);
        var file = await source.Get<BackupService>().CreateInFolderAsync(null, CancellationToken.None);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var bytes = await File.ReadAllBytesAsync(file);
        System.Text.Encoding.ASCII.GetBytes("TMCBAK02").CopyTo(bytes, 0);
        await File.WriteAllBytesAsync(file, bytes);

        using var target = new DesktopHarness();
        var text = target.Get<TMConcept.Desktop.Localization.Localizer>();
        text.Use("tr");
        var model = target.Get<FirstRunViewModel>();
        model.Step = FirstRunStep.Restore;
        model.UseMasterPassword = true;
        model.MasterPassword = model.MasterPasswordRepeat = Master;
        model.BackupFile = file;
        model.BackupPassword = BackupPassword;

        await model.RestoreAsync();

        model.Message.ShouldBe(text["restore.refused"]);
        model.Message.ShouldNotContain("format");
        model.Message.ShouldNotContain("güncelle");
        model.Status.ShouldBeEmpty();
        target.Get<Vault>().State.ShouldBe(VaultState.NotCreated);
    }

    [AvaloniaFact]
    public async Task RestoreShowsItsStepsAndClearsThemAtTheEnd()
    {
        using var source = new DesktopHarness();
        await CreateAsync(source);
        var file = await source.Get<BackupService>().CreateInFolderAsync(null, CancellationToken.None);

        using var target = new DesktopHarness(s => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<TMConcept.Desktop.Dialogs.IConfirmDialog>(s, new FakeConfirm()));
        var text = target.Get<TMConcept.Desktop.Localization.Localizer>();
        var model = target.Get<FirstRunViewModel>();
        var seen = new List<string>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FirstRunViewModel.Status) && model.Status.Length > 0)
            {
                seen.Add(model.Status);
            }
        };
        model.Step = FirstRunStep.Restore;
        model.UseMasterPassword = true;
        model.MasterPassword = model.MasterPasswordRepeat = Master;
        model.BackupFile = file;
        model.BackupPassword = BackupPassword;

        await model.RestoreAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        target.Get<Vault>().State.ShouldBe(VaultState.Open, model.Message);
        seen.ShouldContain(text["restore.stage.opening"]);
        seen.ShouldContain(text["restore.stage.installing"]);
        model.Status.ShouldBeEmpty();
    }
}
