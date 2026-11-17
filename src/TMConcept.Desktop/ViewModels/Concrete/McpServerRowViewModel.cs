using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Research;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A saved MCP server on Settings → Research: its name, what it is (an address or a program), its tools and whether runs use it.</summary>
public sealed partial class McpServerRowViewModel(McpServerSettings server, string info) : ObservableObject
{
    /// <summary>The saved server.</summary>
    public McpServerSettings Server { get; } = server;

    /// <summary>Its name.</summary>
    public string Name => Server.Name;

    /// <summary>"HTTP · https://… · search: web_search(query)".</summary>
    public string Info { get; } = info;

    /// <summary>Research runs use it (saved at once).</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; } = server.Enabled;
}
