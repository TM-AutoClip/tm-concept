using System.Text.Json;

namespace TMConcept.Storage.Keys;

/// <summary>Reads and writes the key file atomically (write to a temporary file, then replace).</summary>
public static class KeyFile
{
    /// <summary>The envelope; <c>null</c> when the file does not exist.</summary>
    public static KeyEnvelope? Read(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), StorageJsonContext.Default.KeyEnvelope)
                ?? throw new InvalidDataException("The key file is empty or damaged.")
            : null;

    /// <summary>Writes the envelope.</summary>
    public static void Write(string path, KeyEnvelope envelope)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(envelope, StorageJsonContext.Default.KeyEnvelope));
        File.Move(temporary, path, overwrite: true);
    }
}
