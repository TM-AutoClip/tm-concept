using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Research;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Documents.Mcp;
using TMConcept.Documents.Research;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Settings → Research (0.1.22), for "Araştırmadan" on the concept page: which backends a run asks (Wikipedia and Wikidata, no key, on by
/// default; Tavily with the user's OWN key, stored encrypted like every API key and never logged), and, folded under "Advanced", the
/// user's MCP servers (off by default): a Streamable HTTP address or a program on this computer, its search tool and optionally its fetch
/// tool. Before a server is first started or reached the user confirms a clear warning (a third-party server runs with their rights and
/// sees the queries); a program is started without the app's environment. Every failure is shown here and logged.
/// </summary>
public sealed partial class ResearchSettingsViewModel(AppSession session, IApiKeyStore keys, McpConnector connector, IConfirmDialog confirm, Localizer text,
    ILogger<ResearchSettingsViewModel> logger) : SettingsSectionBase
{
    private bool _loading;

    /// <inheritdoc />
    public override string TitleKey => "settings.research";

    /// <inheritdoc />
    public override int Order => 22;

    /// <summary>Wikipedia is asked.</summary>
    [ObservableProperty]
    public partial bool Wikipedia { get; set; } = true;

    /// <summary>Wikidata is asked.</summary>
    [ObservableProperty]
    public partial bool Wikidata { get; set; } = true;

    /// <summary>Tavily is asked (with a saved key).</summary>
    [ObservableProperty]
    public partial bool Tavily { get; set; } = true;

    /// <summary>A Tavily key is saved.</summary>
    [ObservableProperty]
    public partial bool HasTavilyKey { get; set; }

    /// <summary>The Tavily key being typed (cleared once saved; the saved one is never shown).</summary>
    [ObservableProperty]
    public partial string TavilyKey { get; set; } = string.Empty;

    /// <summary>The user's MCP servers are used.</summary>
    [ObservableProperty]
    public partial bool McpEnabled { get; set; }

    /// <summary>The saved MCP servers.</summary>
    public ObservableCollection<McpServerRowViewModel> Servers { get; } = [];

    /// <summary>The transports offered ("HTTP address", "Program on this computer").</summary>
    public IReadOnlyList<string> Transports { get; } = [text["research.mcp.http"], text["research.mcp.stdio"]];

    /// <summary>The new server's name.</summary>
    [ObservableProperty]
    public partial string ServerName { get; set; } = string.Empty;

    /// <summary>The new server's transport (0 = HTTP, 1 = program).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHttp), nameof(IsStdio))]
    public partial int TransportIndex { get; set; }

    /// <summary>The new server is an address.</summary>
    public bool IsHttp => TransportIndex == 0;

    /// <summary>The new server is a program.</summary>
    public bool IsStdio => TransportIndex == 1;

    /// <summary>The new server's address.</summary>
    [ObservableProperty]
    public partial string ServerUrl { get; set; } = string.Empty;

    /// <summary>The new server's program.</summary>
    [ObservableProperty]
    public partial string ServerCommand { get; set; } = string.Empty;

    /// <summary>The new server's arguments.</summary>
    [ObservableProperty]
    public partial string ServerArguments { get; set; } = string.Empty;

    /// <summary>The search tool.</summary>
    [ObservableProperty]
    public partial string SearchTool { get; set; } = string.Empty;

    /// <summary>The search tool's query argument.</summary>
    [ObservableProperty]
    public partial string SearchArgument { get; set; } = "query";

    /// <summary>The fetch tool (optional).</summary>
    [ObservableProperty]
    public partial string FetchTool { get; set; } = string.Empty;

    /// <summary>The fetch tool's address argument.</summary>
    [ObservableProperty]
    public partial string FetchArgument { get; set; } = "url";

    /// <summary>The tools the new server listed ("web_search(query), fetch(url)").</summary>
    [ObservableProperty]
    public partial string ToolsLine { get; set; } = string.Empty;

    /// <summary>The MCP part's status line.</summary>
    [ObservableProperty]
    public partial string McpStatus { get; set; } = string.Empty;

    /// <summary>The MCP status is an error.</summary>
    [ObservableProperty]
    public partial bool McpFailed { get; set; }

    /// <summary>Connecting to a server.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>The user confirmed the warning for the server in the form (asked again when its program or address changes).</summary>
    private string _confirmedFor = string.Empty;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        _loading = true;
        try
        {
            var research = session.Settings.Research;
            Wikipedia = research.Wikipedia;
            Wikidata = research.Wikidata;
            Tavily = research.Tavily;
            McpEnabled = research.McpEnabled;
            Rows();
        }
        finally
        {
            _loading = false;
        }

        HasTavilyKey = (await keys.ListAsync(TavilyResearchBackend.KeyProvider, CancellationToken.None).ConfigureAwait(true)).Count > 0;
    }

    /// <summary>Saves the Tavily key (encrypted; replaces the one saved before). It is not tested here: a test would spend the user's allowance.</summary>
    [RelayCommand]
    public async Task SaveTavilyKeyAsync()
    {
        var secret = TavilyKey.Trim();
        if (secret.Length < 8)
        {
            Status = text["keys.invalid"];
            return;
        }

        try
        {
            foreach (var old in await keys.ListAsync(TavilyResearchBackend.KeyProvider, CancellationToken.None).ConfigureAwait(true))
            {
                await keys.DeleteAsync(old.Id, CancellationToken.None).ConfigureAwait(true);
            }

            await keys.SaveAsync(new ApiKey { Provider = TavilyResearchBackend.KeyProvider, Label = "Tavily", Secret = secret }, CancellationToken.None).ConfigureAwait(true);
            TavilyKey = string.Empty;
            HasTavilyKey = true;
            Status = text["research.tavily.saved"];
            logger.LogInformation("The Tavily key was saved");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The Tavily key could not be saved");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>Deletes the saved Tavily key.</summary>
    [RelayCommand]
    public async Task DeleteTavilyKeyAsync()
    {
        try
        {
            foreach (var old in await keys.ListAsync(TavilyResearchBackend.KeyProvider, CancellationToken.None).ConfigureAwait(true))
            {
                await keys.DeleteAsync(old.Id, CancellationToken.None).ConfigureAwait(true);
            }

            HasTavilyKey = false;
            Status = text["research.tavily.deleted"];
            logger.LogInformation("The Tavily key was deleted");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The Tavily key could not be deleted");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>
    /// Connects to the server in the form (after the warning) and lists its tools; a tool whose name says search, or fetch, is filled in
    /// with its argument. Nothing is saved yet.
    /// </summary>
    [RelayCommand]
    public async Task ListToolsAsync()
    {
        if (Busy || Form() is not { } server || !await ConfirmAsync(server).ConfigureAwait(true))
        {
            return;
        }

        Busy = true;
        McpFailed = false;
        McpStatus = text["research.mcp.connecting"];
        try
        {
            await using var client = await connector.ConnectAsync(server, CancellationToken.None).ConfigureAwait(true);
            var tools = await client.ListToolsAsync(CancellationToken.None).ConfigureAwait(true);
            ToolsLine = tools.Count == 0 ? text["research.mcp.no_tools"] : string.Join(", ", tools.Select(t => $"{t.Name}({string.Join(", ", t.Arguments)})"));
            if (SearchTool.Trim().Length == 0 && tools.FirstOrDefault(t => t.Name.Contains("search", StringComparison.OrdinalIgnoreCase)) is { } search)
            {
                SearchTool = search.Name;
                SearchArgument = Argument(search, "query", "q");
            }

            if (FetchTool.Trim().Length == 0 && tools.FirstOrDefault(t => t.Name.Contains("fetch", StringComparison.OrdinalIgnoreCase)
                || t.Name.Contains("extract", StringComparison.OrdinalIgnoreCase)) is { } fetch)
            {
                FetchTool = fetch.Name;
                FetchArgument = Argument(fetch, "url", "urls");
            }

            McpStatus = text.Format("research.mcp.connected", client.ServerName.Length > 0 ? client.ServerName : server.Name, tools.Count);
        }
        catch (McpException ex)
        {
            logger.LogWarning("An MCP server could not be listed ({Code})", ex.Code);
            McpFailed = true;
            McpStatus = text.Format("research.mcp.failed", McpReason(ex));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or InvalidOperationException)
        {
            logger.LogError(ex, "An MCP server could not be listed");
            McpFailed = true;
            McpStatus = text.Format("research.mcp.failed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Saves the server in the form (after the warning) and clears the form.</summary>
    [RelayCommand]
    public async Task AddServerAsync()
    {
        if (Form() is not { } server)
        {
            return;
        }

        if (server.SearchTool.Length == 0)
        {
            McpFailed = true;
            McpStatus = text["research.mcp.no_search_tool"];
            return;
        }

        if (session.Settings.Research.McpServers.Count >= ResearchLimits.McpServersMax)
        {
            McpFailed = true;
            McpStatus = text.Format("research.mcp.too_many", ResearchLimits.McpServersMax);
            return;
        }

        if (!await ConfirmAsync(server).ConfigureAwait(true))
        {
            return;
        }

        var settings = session.Settings;
        settings.Research.McpServers.Add(server);
        await SaveAsync().ConfigureAwait(true);
        ServerName = ServerUrl = ServerCommand = ServerArguments = SearchTool = FetchTool = ToolsLine = string.Empty;
        SearchArgument = "query";
        FetchArgument = "url";
        _confirmedFor = string.Empty;
        McpFailed = false;
        McpStatus = text.Format("research.mcp.added", server.Name);
        logger.LogInformation("An MCP server was added ({Transport})", server.Transport);
        Rows();
    }

    /// <summary>Deletes a saved server.</summary>
    [RelayCommand]
    public async Task DeleteServerAsync(McpServerRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        session.Settings.Research.McpServers.RemoveAll(s => s.Id == row.Server.Id);
        await SaveAsync().ConfigureAwait(true);
        McpStatus = text.Format("research.mcp.deleted", row.Name);
        logger.LogInformation("An MCP server was deleted");
        Rows();
    }

    partial void OnWikipediaChanged(bool value) => _ = SaveChoicesAsync();

    partial void OnWikidataChanged(bool value) => _ = SaveChoicesAsync();

    partial void OnTavilyChanged(bool value) => _ = SaveChoicesAsync();

    partial void OnMcpEnabledChanged(bool value) => _ = SaveChoicesAsync();

    private static string Argument(McpTool tool, params string[] preferred) =>
        preferred.FirstOrDefault(p => tool.Arguments.Contains(p, StringComparer.OrdinalIgnoreCase)) is { } found
            ? tool.Arguments.First(a => a.Equals(found, StringComparison.OrdinalIgnoreCase))
            : tool.Required.FirstOrDefault() ?? tool.Arguments.FirstOrDefault() ?? preferred[0];

    private string McpReason(McpException ex) => text.Has("research.mcp.fail." + ex.Code) ? text.Format("research.mcp.fail." + ex.Code, ex.Message) : ex.Message;

    /// <summary>The server in the form; null (with the reason) when something is missing.</summary>
    private McpServerSettings? Form()
    {
        var name = ServerName.Trim();
        var stdio = IsStdio;
        if (name.Length == 0 || (stdio ? ServerCommand.Trim().Length == 0 : !Uri.TryCreate(ServerUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            McpFailed = true;
            McpStatus = text["research.mcp.form_missing"];
            return null;
        }

        return new McpServerSettings
        {
            Name = name,
            Transport = stdio ? McpTransportKind.Stdio : McpTransportKind.Http,
            Command = stdio ? ServerCommand.Trim() : string.Empty,
            Arguments = stdio ? ServerArguments.Trim() : string.Empty,
            Url = stdio ? string.Empty : ServerUrl.Trim(),
            SearchTool = SearchTool.Trim(),
            SearchArgument = SearchArgument.Trim().Length > 0 ? SearchArgument.Trim() : "query",
            FetchTool = FetchTool.Trim(),
            FetchArgument = FetchArgument.Trim().Length > 0 ? FetchArgument.Trim() : "url",
            Enabled = true,
        };
    }

    /// <summary>The warning before a server is first started or reached; once per program or address of the form.</summary>
    private async Task<bool> ConfirmAsync(McpServerSettings server)
    {
        var what = server.Transport == McpTransportKind.Stdio ? server.Command + " " + server.Arguments : server.Url;
        if (_confirmedFor == what)
        {
            server.Confirmed = true;
            return true;
        }

        var answer = await confirm.AskAsync(text["research.mcp.warning.title"], text.Format("research.mcp.warning", what.Trim()), text["research.mcp.warning.accept"], danger: true)
            .ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return false;
        }

        _confirmedFor = what;
        server.Confirmed = true;
        logger.LogInformation("The warning for an MCP server was accepted ({Transport})", server.Transport);
        return true;
    }

    private void Rows()
    {
        foreach (var row in Servers)
        {
            row.PropertyChanged -= OnRow;
        }

        Servers.Clear();
        foreach (var server in session.Settings.Research.McpServers)
        {
            var where = server.Transport == McpTransportKind.Stdio ? (server.Command + " " + server.Arguments).Trim() : server.Url;
            var info = string.Join(" · ", new[]
            {
                text[server.Transport == McpTransportKind.Stdio ? "research.mcp.stdio" : "research.mcp.http"],
                where,
                text.Format("research.mcp.tools", server.SearchTool + "(" + server.SearchArgument + ")",
                    server.FetchTool.Length > 0 ? server.FetchTool + "(" + server.FetchArgument + ")" : "—"),
            });
            var row = new McpServerRowViewModel(server, info);
            row.PropertyChanged += OnRow;
            Servers.Add(row);
        }
    }

    private void OnRow(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is McpServerRowViewModel row && e.PropertyName == nameof(McpServerRowViewModel.Enabled))
        {
            row.Server.Enabled = row.Enabled;
            _ = SaveChoicesAsync();
        }
    }

    private async Task SaveChoicesAsync()
    {
        if (_loading)
        {
            return;
        }

        var research = session.Settings.Research;
        research.Wikipedia = Wikipedia;
        research.Wikidata = Wikidata;
        research.Tavily = Tavily;
        research.McpEnabled = McpEnabled;
        await SaveAsync().ConfigureAwait(true);
    }

    private async Task SaveAsync()
    {
        try
        {
            await session.SaveAsync(session.Settings, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The research settings could not be saved");
            Status = text.Format("error.generic", ex.Message);
        }
    }
}
