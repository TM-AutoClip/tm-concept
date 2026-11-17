using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// The models in the app's own Ollama models folder, read from the disk without the server (Ollama's own layout: a manifest per tag at
/// <c>manifests/registry.ollama.ai/&lt;namespace&gt;/&lt;name&gt;/&lt;tag&gt;</c> listing its layers, each layer a content-addressed blob
/// <c>blobs/sha256-&lt;hex&gt;</c>). A model is installed when its manifest is there and every blob it names is on disk with its size. Removing a
/// model takes its manifest and only the blobs no other model uses (<see cref="OwnedPaths"/>). Only a well-formed tag is ever turned into a
/// path, so a tag can never point outside the models folder.
/// </summary>
public sealed partial class AppModelStore(AppOllamaLayout layout)
{
    private const string Registry = "registry.ollama.ai";
    private const string DefaultNamespace = "library";

    /// <summary>The models folder (<c>OLLAMA_MODELS</c>).</summary>
    public string ModelsDirectory => layout.ModelsDirectory;

    /// <summary>Whether <paramref name="tag"/> is a well-formed Ollama tag (<c>name:tag</c> or <c>namespace/name:tag</c>, no path parts).</summary>
    public static bool IsValidTag(string? tag) => tag is not null && TagPattern().IsMatch(tag) && !tag.Contains("..", StringComparison.Ordinal);

    /// <summary>The manifest file of <paramref name="tag"/>; throws for a tag that is not well-formed.</summary>
    public string ManifestPath(string tag)
    {
        if (!IsValidTag(tag))
        {
            throw new ArgumentException("Not an Ollama model tag.", nameof(tag));
        }

        var colon = tag.LastIndexOf(':');
        var name = colon > 0 ? tag[..colon] : tag;
        var version = colon > 0 ? tag[(colon + 1)..] : "latest";
        var parts = name.Split('/');
        var space = parts.Length > 1 ? parts[0] : DefaultNamespace;
        return Path.Combine(layout.ModelsDirectory, "manifests", Registry, space, parts[^1], version);
    }

    /// <summary>The file of a blob (<c>sha256:…</c> → <c>blobs/sha256-…</c>); null for a digest that is not a SHA-256.</summary>
    public string? BlobPath(string digest) =>
        DigestPattern().IsMatch(digest ?? string.Empty) ? Path.Combine(layout.ModelsDirectory, "blobs", digest!.Replace(':', '-')) : null;

    /// <summary>Whether <paramref name="tag"/> has a manifest (finished or not).</summary>
    public bool HasManifest(string tag) => IsValidTag(tag) && File.Exists(ManifestPath(tag));

    /// <summary>Whether <paramref name="tag"/> is installed and complete.</summary>
    public bool IsInstalled(string tag) => HasManifest(tag) && Read(tag) is { Complete: true };

    /// <summary>The model of <paramref name="tag"/>; null when it has no readable manifest.</summary>
    public InstalledAppModel? Read(string tag)
    {
        if (!HasManifest(tag))
        {
            return null;
        }

        return Parse(tag, ManifestPath(tag));
    }

    /// <summary>Every model with a readable manifest (complete or not), by tag.</summary>
    public IReadOnlyList<InstalledAppModel> List()
    {
        var root = Path.Combine(layout.ModelsDirectory, "manifests", Registry);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var models = new List<InstalledAppModel>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/').Split('/');
                if (relative.Length != 3)
                {
                    continue;
                }

                var tag = (relative[0] == DefaultNamespace ? relative[1] : relative[0] + "/" + relative[1]) + ":" + relative[2];
                if (IsValidTag(tag) && Parse(tag, file) is { } model)
                {
                    models.Add(model);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read lists nothing more; what was read stays.
        }

        return models.OrderBy(m => m.Tag, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// What removing <paramref name="tag"/> takes away: its manifest and every blob it uses that no other model's manifest names (shared
    /// layers stay for the other model). Only paths that exist.
    /// </summary>
    public IReadOnlyList<string> OwnedPaths(string tag)
    {
        if (!HasManifest(tag))
        {
            return [];
        }

        var model = Read(tag);
        var others = List().Where(m => !string.Equals(m.Tag, tag, StringComparison.Ordinal)).SelectMany(m => m.Digests).ToHashSet(StringComparer.Ordinal);
        var paths = new List<string> { ManifestPath(tag) };
        foreach (var digest in model?.Digests ?? [])
        {
            if (!others.Contains(digest) && BlobPath(digest) is { } blob && File.Exists(blob))
            {
                paths.Add(blob);
            }
        }

        return paths;
    }

    /// <summary>
    /// The files in <c>blobs/</c> that no manifest names: the received parts of a pull that was cancelled (<c>sha256-…-partial*</c>; the app's
    /// server never prunes them itself, so a pull can go on from there) and layers no model uses any more. Every manifest under
    /// <c>manifests/</c> counts, of any registry and complete or not. When one manifest cannot be read, or the folder cannot be listed,
    /// nothing is named (that manifest might use any blob).
    /// </summary>
    public IReadOnlyList<string> UnreferencedBlobs()
    {
        var blobs = Path.Combine(layout.ModelsDirectory, "blobs");
        if (!Directory.Exists(blobs))
        {
            return [];
        }

        var named = new HashSet<string>(StringComparer.Ordinal);
        var manifests = Path.Combine(layout.ModelsDirectory, "manifests");
        try
        {
            if (Directory.Exists(manifests))
            {
                foreach (var file in Directory.EnumerateFiles(manifests, "*", SearchOption.AllDirectories))
                {
                    if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject manifest)
                    {
                        return [];
                    }

                    var layers = (manifest["layers"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
                    if (manifest["config"] is JsonObject config)
                    {
                        layers.Add(config);
                    }

                    foreach (var layer in layers)
                    {
                        if (layer["digest"]?.GetValueKind() == JsonValueKind.String)
                        {
                            named.Add(layer["digest"]!.GetValue<string>().Replace(':', '-'));
                        }
                    }
                }
            }

            return Directory.EnumerateFiles(blobs).Where(f => !named.Contains(Path.GetFileName(f))).Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A manifest that cannot be read might name any blob: none is called unused.
            return [];
        }
    }

    /// <summary>Bytes of the files <see cref="OwnedPaths"/> names (what a removal frees).</summary>
    public long OwnedBytes(string tag) => OwnedPaths(tag).Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);

    private InstalledAppModel? Parse(string tag, string file)
    {
        JsonNode? manifest;
        try
        {
            manifest = JsonNode.Parse(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        if (manifest is not JsonObject root)
        {
            return null;
        }

        var layers = (root["layers"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        if (root["config"] is JsonObject config)
        {
            layers.Add(config);
        }

        var digests = new List<string>();
        long bytes = 0;
        var complete = layers.Count > 0;
        foreach (var layer in layers)
        {
            var digest = layer["digest"]?.GetValueKind() == JsonValueKind.String ? layer["digest"]!.GetValue<string>() : string.Empty;
            var size = layer["size"]?.GetValueKind() == JsonValueKind.Number ? layer["size"]!.GetValue<long>() : 0;
            if (BlobPath(digest) is not { } blob)
            {
                complete = false;
                continue;
            }

            digests.Add(digest);
            bytes += size;
            if (!File.Exists(blob) || new FileInfo(blob).Length != size)
            {
                complete = false;
            }
        }

        return new InstalledAppModel(tag, bytes, digests, complete);
    }

    [GeneratedRegex(@"^(?:[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*(?::[A-Za-z0-9][A-Za-z0-9._-]*)?$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$")]
    private static partial Regex DigestPattern();
}
