using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The splash window (0.1.38): "Starting TM Concept…", the stage now running and how far the start is, Cancel (ends the app). It depends on
/// no service: its texts come from a <see cref="Localizer"/> made before the services, and the steps report to it (<see cref="IStartupScreen"/>).
/// </summary>
public sealed partial class SplashViewModel : ObservableObject, IStartupScreen
{
    private readonly Localizer _text;
    private readonly IStartupSequence _sequence;

    /// <summary>Creates the model with the texts of the app's language.</summary>
    public SplashViewModel(Localizer text, IStartupSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _sequence = sequence;
        Title = text["splash.title"];
        StageText = text["splash.stage.preparing"];
        CancelText = text["common.cancel"];
    }

    /// <summary>The number of stages (the progress bar's end).</summary>
    public static double StageCount { get; } = Enum.GetValues<StartupStage>().Length;

    /// <summary>"Starting TM Concept…".</summary>
    public string Title { get; }

    /// <summary>The version under the title.</summary>
    public string Version { get; } = "v" + Logging.AppIdentity.ShortVersion;

    /// <summary>The stage now running.</summary>
    [ObservableProperty]
    public partial string StageText { get; set; }

    /// <summary>How many stages have begun (the progress bar's value).</summary>
    [ObservableProperty]
    public partial int StageNumber { get; set; } = 1;

    /// <summary>The stage that began last.</summary>
    public StartupStage Stage { get; private set; }

    /// <summary>Why the start stopped (empty while it runs).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether the start stopped with an error.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>"Cancel", "Close" after an error.</summary>
    [ObservableProperty]
    public partial string CancelText { get; set; }

    /// <inheritdoc />
    public void Report(StartupStage value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Show(value);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Show(value));
        }
    }

    /// <inheritdoc />
    public void ShowError(StartupStage stage, string message)
    {
        StageText = StageName(stage);
        Error = _text.Format("splash.failed", StageName(stage), message);
        CancelText = _text["common.close"];
    }

    /// <summary>The name of <paramref name="stage"/> on the splash window.</summary>
    public string StageName(StartupStage stage) => _text[stage switch
    {
        StartupStage.Preparing => "splash.stage.preparing",
        StartupStage.Components => "splash.stage.components",
        StartupStage.Vault => "splash.stage.vault",
        StartupStage.Database => "splash.stage.database",
        StartupStage.Session => "splash.stage.session",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    }];

    /// <summary>Ends the app (Cancel, or Close after an error).</summary>
    [RelayCommand]
    private void Cancel() => _sequence.Cancel();

    private void Show(StartupStage stage)
    {
        if (HasError)
        {
            return;
        }

        Stage = stage;
        StageText = StageName(stage);
        StageNumber = Math.Max(StageNumber, (int)stage + 1);
    }
}
