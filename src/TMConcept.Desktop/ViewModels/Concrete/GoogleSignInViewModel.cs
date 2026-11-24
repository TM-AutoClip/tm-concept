using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Providers;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Providers.GoogleAuth;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// "Sign in with Google" in Settings → Keys → Google Cloud (0.1.47, H3 decision A; the API key form stays as C). The user picks the client
/// file Google downloaded for their own "Desktop app" client and types the project id; "Sign in with Google" opens the browser and waits
/// for it without holding the window (the address is shown to copy, also when the browser could not be opened); Cancel ends the wait.
/// "Sign out" deletes the sign-in here and revokes it at Google. The help (<see cref="Steps"/>) walks through Google's pages, each with a
/// button to open it; when Google changes its screens only these texts change.
/// </summary>
public sealed partial class GoogleSignInViewModel(GoogleCloudSignIn signIn, FileDialogs dialogs, ShellLauncher shell, WindowHost host, Localizer text,
    ILogger<GoogleSignInViewModel> logger) : ObservableObject
{
    private static readonly (string Key, string? Link)[] HelpPages =
    [
        ("google.help.1", "https://console.cloud.google.com/projectcreate"),
        ("google.help.2", "https://console.cloud.google.com/billing"),
        ("google.help.3", "https://console.cloud.google.com/apis/library/texttospeech.googleapis.com"),
        ("google.help.4", "https://console.cloud.google.com/auth/overview"),
        ("google.help.5", "https://console.cloud.google.com/auth/audience"),
        ("google.help.6", "https://console.cloud.google.com/auth/clients/create"),
        ("google.help.7", null),
        ("google.help.8", null),
        ("google.help.9", "https://console.cloud.google.com/auth/audience"),
    ];

    private GoogleOAuthClient? _client;
    private CancellationTokenSource? _running;

    /// <summary>The help steps in the current language.</summary>
    public IReadOnlyList<HelpStep> Steps => [.. HelpPages.Select((p, i) => new HelpStep(i + 1, text[p.Key], p.Link is null ? null : new Uri(p.Link)))];

    /// <summary>The name of the chosen client file (empty = none).</summary>
    [ObservableProperty]
    public partial string ClientFile { get; set; } = string.Empty;

    /// <summary>The Google Cloud project billed for the voices.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    public partial string ProjectId { get; set; } = string.Empty;

    /// <summary>Waiting for the browser.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn), nameof(HasAddress))]
    public partial bool Busy { get; set; }

    /// <summary>The sign-in address while waiting (to copy when the browser did not open).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddress))]
    public partial string Address { get; set; } = string.Empty;

    /// <summary>Whether the address is shown.</summary>
    public bool HasAddress => Busy && Address.Length > 0;

    /// <summary>Signed in now.</summary>
    [ObservableProperty]
    public partial bool SignedIn { get; set; }

    /// <summary>The state line (signed in with which project since when, or not signed in).</summary>
    [ObservableProperty]
    public partial string State { get; set; } = string.Empty;

    /// <summary>The result of the last action.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Status"/> is an error (red).</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Whether a client file is read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    public partial bool HasClient { get; set; }

    /// <summary>Whether "Sign in with Google" can start.</summary>
    public bool CanSignIn => HasClient && ProjectId.Trim().Length > 0 && !Busy;

    /// <summary>Reads the sign-in state (database work off the window's thread).</summary>
    public async Task ReloadAsync()
    {
        OnPropertyChanged(nameof(Steps));
        try
        {
            var record = await signIn.RecordAsync(CancellationToken.None).ConfigureAwait(true);
            SignedIn = record is not null;
            if (record is not null)
            {
                if (ProjectId.Length == 0)
                {
                    ProjectId = record.ProjectId;
                }

                State = "✓ " + text.Format("google.state.in", record.ProjectId, record.SignedInAtUtc.ToLocalTime().ToString("g", text.Culture));
            }
            else
            {
                State = text["google.state.out"];
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The Google sign-in state could not be read");
            State = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>Picks the client file and reads it (a web client or another file is refused with a clear text).</summary>
    [RelayCommand]
    public async Task ChooseFileAsync()
    {
        var path = await dialogs.OpenAsync("google.file.title", "google.file.filter", ["*.json"]).ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        try
        {
            _client = await GoogleOAuthClientFile.ReadAsync(path, CancellationToken.None).ConfigureAwait(true);
            HasClient = true;
            ClientFile = Path.GetFileName(path);
            Failed = false;
            Status = "✓ " + text["google.file.ok"];
            logger.LogInformation("Google sign-in: a desktop client file was read");
        }
        catch (GoogleSignInException ex)
        {
            Refuse(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _client = null;
            HasClient = false;
            ClientFile = string.Empty;
            Failed = true;
            Status = text.Format("error.generic", ex.Message);
            logger.LogWarning(ex, "Google sign-in: the client file could not be read");
        }
    }

    /// <summary>Signs in: the browser opens, the window stays free; Cancel ends the wait.</summary>
    [RelayCommand]
    public async Task SignInAsync()
    {
        if (_client is not { } client || !CanSignIn)
        {
            return;
        }

        using var running = new CancellationTokenSource();
        _running = running;
        Busy = true;
        Failed = false;
        Status = text["google.waiting"];
        try
        {
            await signIn.SignInAsync(client, ProjectId, OpenBrowser, address => Address = address.AbsoluteUri,
                new LoopbackPages(text["google.page.done"], text["google.page.failed"]), running.Token).ConfigureAwait(true);
            Status = "✓ " + text["google.signed_in"];
        }
        catch (OperationCanceledException)
        {
            Status = text["google.canceled"];
            logger.LogInformation("Google sign-in canceled");
        }
        catch (GoogleSignInException ex)
        {
            Failed = true;
            Status = "✗ " + Text(ex.Code);
            logger.LogWarning("Google sign-in failed ({Code})", ex.Code);
        }
        catch (ProviderException ex)
        {
            Failed = true;
            Status = "✗ " + text.Format("google.fail.exchange", ProviderErrors.Text(text, ex.Code));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The local listener could not start (SocketException) or the sign-in could not be saved: the page says so, the app goes on.
            Failed = true;
            Status = "✗ " + text.Format("error.generic", ex.Message);
            logger.LogError(ex, "Google sign-in failed");
        }
        finally
        {
            _running = null;
            Busy = false;
            Address = string.Empty;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Ends the wait for the browser.</summary>
    [RelayCommand]
    public void Cancel() => _running?.Cancel();

    /// <summary>Signs out (here first, then at Google).</summary>
    [RelayCommand]
    public async Task SignOutAsync()
    {
        try
        {
            var revoked = await signIn.SignOutAsync(CancellationToken.None).ConfigureAwait(true);
            Failed = false;
            Status = revoked ? "✓ " + text["google.signed_out"] : text["google.signed_out_local"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Google sign-out failed");
            Failed = true;
            Status = text.Format("error.generic", ex.Message);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Copies the sign-in address.</summary>
    [RelayCommand]
    public async Task CopyAddressAsync()
    {
        if (Address.Length > 0 && host.Active?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(Address).ConfigureAwait(true);
            logger.LogInformation("Google sign-in: the address was copied");
        }
    }

    /// <summary>Opens the Google page of a help step.</summary>
    [RelayCommand]
    public void OpenStep(HelpStep? step)
    {
        if (step?.Link is { } link)
        {
            OpenBrowser(link);
        }
    }

    private bool OpenBrowser(Uri address)
    {
        try
        {
            shell.Open(address);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = text["google.no_browser"];
            return false;
        }
    }

    private void Refuse(GoogleSignInException ex)
    {
        _client = null;
        HasClient = false;
        ClientFile = string.Empty;
        Failed = true;
        Status = "✗ " + Text(ex.Code);
        logger.LogWarning("Google sign-in: the client file was refused ({Code})", ex.Code);
    }

    private string Text(string code) => code is "file.json" or "file.web" or "file.kind" or "file.fields" or "project" or "state" or "denied" or "code"
        or "timeout" or "no_refresh"
        ? text["google.fail." + code]
        : text["google.fail.other"];
}
