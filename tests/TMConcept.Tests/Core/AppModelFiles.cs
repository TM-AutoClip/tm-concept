using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.AppOllama;

namespace TMConcept.Tests.Core;

/// <summary>Writes a model into an app Ollama models folder the way Ollama lays it out (a manifest and small blobs).</summary>
internal static class AppModelFiles
{
    /// <summary>Writes <paramref name="tag"/> with the blobs named by their contents (a content shared by two models is one blob).</summary>
    public static IReadOnlyList<string> Write(AppModelStore store, string tag, params string[] contents)
    {
        var digests = new List<string>();
        var layers = new JsonArray();
        foreach (var content in contents.Prepend("config of " + tag))
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
            var blob = store.BlobPath(digest)!;
            Directory.CreateDirectory(Path.GetDirectoryName(blob)!);
            File.WriteAllBytes(blob, bytes);
            digests.Add(digest);
            layers.Add(new JsonObject { ["mediaType"] = "application/vnd.ollama.image.model", ["digest"] = digest, ["size"] = bytes.Length });
        }

        var config = layers[0]!.DeepClone();
        layers.RemoveAt(0);
        var manifest = store.ManifestPath(tag);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, new JsonObject { ["schemaVersion"] = 2, ["config"] = config, ["layers"] = layers }.ToJsonString());
        return digests;
    }
}
