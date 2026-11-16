using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Endpoints;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The "local server (OpenAI-compatible)" part of Settings → API keys: name, address, optional token; "Connect and list models" asks the
/// server (<c>GET {base}/models</c>, a short wait) and lists its models with their limits; a model id can also be typed. Save keeps the
/// endpoint with the ticked models (at least one), whose models every text step then offers, free and without a key. The saved servers have
/// a test (the same model list), an edit (the form is filled with the server; saving keeps its id, so every step that chose one of its
/// models keeps it: deleting and adding it again gave it a new id and those choices were lost) and a delete with confirmation. Every failure
/// is said in red in the current language; the token is never logged or shown whole.
/// </summary>
public sealed partial class EndpointEditorViewModel(EndpointRegistry endpoints, IEndpointProbe probe, IConfirmDialog confirm, Localizer text,
    ILogger<EndpointEditorViewModel> logger) : ObservableObject
{
    /// <summary>The server's models (and typed ones) for the endpoint being added.</summary>
    public ObservableCollection<EndpointModelRowViewModel> Models { get; } = [];

    /// <summary>Saved servers.</summary>
    public ObservableCollection<EndpointRowViewModel> Saved { get; } = [];

    /// <summary>Name of the new server.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Base address of the new server (<c>http://localhost:13305/api/v1</c>).</summary>
    [ObservableProperty]
    public partial string Address { get; set; } = string.Empty;

    /// <summary>Optional token of the new server.</summary>
    [ObservableProperty]
    public partial string Token { get; set; } = string.Empty;

    /// <summary>A model id typed by hand.</summary>
    [ObservableProperty]
    public partial string ManualModel { get; set; } = string.Empty;

    /// <summary>The result of the last action.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Status"/> is an error (shown in red).</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>A request runs.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>A saved server is being edited (its id and date are kept on save).</summary>
    [ObservableProperty]
    public partial bool Editing { get; private set; }

    // The server being edited (null = a new one).
    private LocalEndpoint? _editing;

    /// <summary>Shows the saved servers.</summary>
    public void Reload()
    {
        Saved.Clear();
        foreach (var endpoint in endpoints.All)
        {
            Saved.Add(new EndpointRowViewModel(endpoint));
        }
    }

    /// <summary>Asks the server for its models and lists them (the ones already ticked or typed stay).</summary>
    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (Busy)
        {
            return;
        }

        if (!EndpointAddress.TryParse(Address, out _))
        {
            Fail(AddressFailure());
            return;
        }

        Busy = true;
        try
        {
            var listing = await probe.ListModelsAsync(Address, Token.Trim(), CancellationToken.None).ConfigureAwait(true);
            if (!listing.Ok)
            {
                logger.LogWarning("A local server could not list its models ({Code})", listing.Code);
                Fail(Failure(listing.Code));
                return;
            }

            var kept = Models.Where(m => m.Picked).ToList();
            Models.Clear();
            foreach (var model in listing.Models)
            {
                // A server with one model (llama-server) gets it ticked at once.
                Models.Add(kept.FirstOrDefault(k => k.Id == model.Id) ?? new EndpointModelRowViewModel(model.Id, model.ContextTokens, listing.Models.Count == 1));
            }

            foreach (var typed in kept.Where(k => listing.Models.All(m => m.Id != k.Id)))
            {
                Models.Add(typed);
            }

            Failed = false;
            Status = "✓ " + text.Format("keys.endpoint.found", listing.Models.Count);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Adds the typed model id to the list (ticked).</summary>
    [RelayCommand]
    public void AddManual()
    {
        var id = ManualModel.Trim();
        if (id.Length == 0)
        {
            return;
        }

        if (Models.FirstOrDefault(m => m.Id == id) is { } existing)
        {
            existing.Picked = true;
        }
        else
        {
            Models.Add(new EndpointModelRowViewModel(id, 0, picked: true));
        }

        ManualModel = string.Empty;
    }

    /// <summary>Saves the new server with its ticked models.</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Busy)
        {
            return;
        }

        if (!EndpointAddress.TryParse(Address, out var address))
        {
            Fail(AddressFailure());
            return;
        }

        var picked = Models.Where(m => m.Picked).Select(m => m.ToModel()).ToList();
        if (picked.Count == 0)
        {
            Fail(text["keys.endpoint.no_model"]);
            return;
        }

        var endpoint = new LocalEndpoint
        {
            Name = Name.Trim().Length > 0 ? Name.Trim() : address.Authority,
            BaseUrl = address.AbsoluteUri.TrimEnd('/'),
            Token = Token.Trim(),
            Models = picked,
        };
        if (_editing is { } edited)
        {
            // The same id: the store and the registry replace it, and the steps' choices (endpoint:<id>/<model>) still point at it.
            endpoint.Id = edited.Id;
            endpoint.CreatedAtUtc = edited.CreatedAtUtc;
        }

        Busy = true;
        try
        {
            await endpoints.SaveAsync(endpoint, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message comes from the storage layer, which never puts the token into a message.
            logger.LogError(ex, "A local server could not be saved");
            Fail(text.Format("error.generic", ex.Message));
            return;
        }
        finally
        {
            Busy = false;
        }

        Clear();
        Failed = false;
        Status = "✓ " + text.Format("keys.endpoint.saved_ok", endpoint.Name, picked.Count);
        Reload();
    }

    /// <summary>Fills the form with a saved server to change it (its models ticked, with their limits); saving replaces it.</summary>
    [RelayCommand]
    public void Edit(EndpointRowViewModel? row)
    {
        if (row is null || Busy)
        {
            return;
        }

        _editing = row.Endpoint;
        Editing = true;
        Name = row.Endpoint.Name;
        Address = row.Endpoint.BaseUrl;
        Token = row.Endpoint.Token;
        Models.Clear();
        foreach (var model in row.Endpoint.Models)
        {
            Models.Add(new EndpointModelRowViewModel(model));
        }

        Failed = false;
        Status = text.Format("keys.endpoint.editing", row.Endpoint.Name);
    }

    /// <summary>Leaves the edit without saving (the form is emptied).</summary>
    [RelayCommand]
    public void CancelEdit()
    {
        Clear();
        Failed = false;
        Status = string.Empty;
    }

    private void Clear()
    {
        _editing = null;
        Editing = false;
        Name = string.Empty;
        Address = string.Empty;
        Token = string.Empty;
        ManualModel = string.Empty;
        Models.Clear();
    }

    /// <summary>Tests a saved server (its model list); a kept model the server does not list any more is named.</summary>
    [RelayCommand]
    public async Task TestAsync(EndpointRowViewModel? row)
    {
        if (row is null || Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            var listing = await probe.ListModelsAsync(row.Endpoint.BaseUrl, row.Endpoint.Token, CancellationToken.None).ConfigureAwait(true);
            row.Failed = !listing.Ok;
            if (!listing.Ok)
            {
                logger.LogWarning("A saved local server failed its test ({Code})", listing.Code);
                row.Result = "✗ " + Failure(listing.Code);
                return;
            }

            var missing = row.Endpoint.Models.Select(m => m.Id).Where(id => listing.Models.All(m => m.Id != id)).ToList();
            row.Result = missing.Count == 0
                ? "✓ " + text["keys.test.ok"]
                : "✓ " + text.Format("keys.endpoint.not_listed", string.Join(", ", missing));
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Deletes a saved server after a confirmation.</summary>
    [RelayCommand]
    public async Task DeleteAsync(EndpointRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["keys.endpoint.delete.title"], text.Format("keys.endpoint.delete.message", row.Name),
            text["common.delete"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        await endpoints.DeleteAsync(row.Endpoint.Id, CancellationToken.None).ConfigureAwait(true);
        Saved.Remove(row);
        if (_editing?.Id == row.Endpoint.Id)
        {
            // Saving the form now would bring the deleted server back.
            CancelEdit();
        }
    }

    /// <summary>Why the typed address is refused: a user name or password in it has its own reason (the token field is the place for it).</summary>
    private string AddressFailure() =>
        EndpointAddress.HasCredentials(Address) ? text["keys.endpoint.fail.credentials"] : text["keys.endpoint.fail.address"];

    private void Fail(string message)
    {
        Failed = true;
        Status = "✗ " + message;
    }

    /// <summary>A listing failure as text: the usual local server problems in their own words, other codes as for any provider.</summary>
    private string Failure(string code) => code switch
    {
        "address" => text["keys.endpoint.fail.address"],
        "network" => text["keys.endpoint.fail.network"],
        "timeout" => text["keys.endpoint.fail.timeout"],
        "auth" or "forbidden" => text["keys.endpoint.fail.auth"],
        "empty" => text["keys.endpoint.fail.empty"],
        "unreadable" or "model" => text["keys.endpoint.fail.unreadable"],
        _ => ProviderErrors.Text(text, code),
    };
}
