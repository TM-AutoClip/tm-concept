using System.Text.Json.Serialization;
using TMConcept.Core.Settings;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Keys;

namespace TMConcept.Storage;

/// <summary>Source-generated JSON for the storage's own documents (no reflection; works with trimming).</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(KeyEnvelope))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(BackupKeyRecord))]
[JsonSerializable(typeof(BackupContents))]
[JsonSerializable(typeof(BackupHeader))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
