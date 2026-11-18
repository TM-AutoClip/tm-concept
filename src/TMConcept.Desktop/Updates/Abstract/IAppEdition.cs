namespace TMConcept.Desktop.Updates;

/// <summary>Which edition this copy is (<see cref="AppEditionKind"/>) and, for the installed one, where it lives.</summary>
public interface IAppEdition
{
    /// <summary>Installed by Setup or the portable program.</summary>
    AppEditionKind Kind { get; }

    /// <summary>The installed version Setup recorded (<c>0.1.26</c>); empty for the portable edition.</summary>
    string InstalledVersion { get; }

    /// <summary>The install folder (<c>%LOCALAPPDATA%\TMConcept</c>, holding <c>Update.exe</c>, <c>current\</c> and <c>packages\</c>); null for the portable edition.</summary>
    string? InstallRoot { get; }

    /// <summary>The Windows app id the installer gave the shortcuts (taskbar grouping); null for the portable edition.</summary>
    string? AppUserModelId { get; }
}
