namespace TMConcept.Desktop.Os;

/// <summary>Linux: no shortcuts are written by the app (the portable archive has its own menu entry script).</summary>
public sealed class UnsupportedShortcutWriter : IShortcutWriter
{
    /// <inheritdoc />
    public bool Supported => false;

    /// <inheritdoc />
    public IReadOnlyList<string> Write(string program, string appUserModelId) =>
        throw new PlatformNotSupportedException("Shortcuts are written on Windows only.");
}
