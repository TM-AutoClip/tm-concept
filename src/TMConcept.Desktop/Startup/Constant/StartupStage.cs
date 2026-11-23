namespace TMConcept.Desktop.Startup;

/// <summary>The steps of the start the splash window names, in their order (0.1.38).</summary>
public enum StartupStage
{
    /// <summary>The services are built, the crash log and the log file are attached.</summary>
    Preparing,

    /// <summary>The kept model catalog is applied and processes an earlier run left are ended.</summary>
    Components,

    /// <summary>The database key is taken out of its protection (keyring).</summary>
    Vault,

    /// <summary>The database is checked and brought to this version's schema (on a working copy).</summary>
    Database,

    /// <summary>The settings, the selected profile and the local servers are read.</summary>
    Session,
}
