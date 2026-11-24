using System.Text.Json.Serialization;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Keys;
using TMConcept.Core.Settings;
using TMConcept.Core.Speech;
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
[JsonSerializable(typeof(List<LocalEndpoint>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SignInRecord))]
[JsonSerializable(typeof(List<AccountVoice>))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
