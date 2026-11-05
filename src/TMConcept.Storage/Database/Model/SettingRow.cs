namespace TMConcept.Storage.Database;

/// <summary>A settings document (key → JSON).</summary>
public sealed class SettingRow
{
    /// <summary>Key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>JSON value.</summary>
    public string Value { get; set; } = string.Empty;
}
