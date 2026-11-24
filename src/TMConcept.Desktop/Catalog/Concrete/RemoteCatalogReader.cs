using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Desktop.Updates;
using TMConcept.Engines.Images;
using TMConcept.Engines.Tools;
using TMConcept.Providers.Catalog;

namespace TMConcept.Desktop.Catalog;

/// <summary>
/// Checks a remote model catalog (<see cref="RemoteCatalogFile"/>) and turns it into entries the app can add. The file is untrusted input:
/// it may only use what the app already has. A cloud model names a provider of the built-in catalog and only capabilities and efforts that
/// provider's built-in models have; an image model runs on stable-diffusion.cpp with a family, sampler and scheduler a built-in model uses
/// (no free command-line arguments), every weights file on <see cref="RemoteCatalogRules.ModelHost"/> (which the download allow-list holds)
/// at a 40-hex commit with its SHA-256 and size; an Ollama model names an exact tag and its manifest digest. Licences are required
/// (anything but free commercial use is asked before the download, as for built-in models). Any broken entry, an unknown field, a higher
/// schema or a <c>minApp</c> above this app refuses the whole file (<see cref="RemoteCatalogException"/>); an id the app has built in is
/// left out (the built-in catalog is the floor); a model needing a newer stable-diffusion.cpp is left out.
/// </summary>
public sealed partial class RemoteCatalogReader(EmbeddedModelCatalog catalog)
{
    /// <summary>Checks <paramref name="json"/> for an app of version <paramref name="appVersion"/>.</summary>
    public RemoteCatalog Read(string json, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > RemoteCatalogRules.MaxFileBytes)
        {
            throw new RemoteCatalogException("the file is larger than " + RemoteCatalogRules.MaxFileBytes.ToString(CultureInfo.InvariantCulture) + " bytes");
        }

        RemoteCatalogFile file;
        try
        {
            file = JsonSerializer.Deserialize(json, RemoteCatalogJsonContext.Default.RemoteCatalogFile)
                ?? throw new RemoteCatalogException("the file is empty");
        }
        catch (JsonException ex)
        {
            throw new RemoteCatalogException("the file is not a model catalog (" + ex.Message + ")", ex);
        }

        if (file.Schema != RemoteCatalogRules.Schema)
        {
            throw new RemoteCatalogException("schema " + file.Schema.ToString(CultureInfo.InvariantCulture) + " is not read by this app");
        }

        if (!DateOnly.TryParseExact(file.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new RemoteCatalogException("its date is not yyyy-MM-dd");
        }

        if (!AppVersion.TryParse(file.MinApp, out var minimum))
        {
            throw new RemoteCatalogException("its minApp is not a version");
        }

        if (AppVersion.TryParse(appVersion, out var running) && minimum.IsNewerThan(running))
        {
            throw new RemoteCatalogException("it needs TM Concept " + minimum + " or newer");
        }

        var providers = file.Providers ?? [];
        var images = file.Images ?? [];
        var ollama = file.Ollama ?? [];
        if (providers.Count > RemoteCatalogRules.MaxEntries || images.Count > RemoteCatalogRules.MaxEntries || ollama.Count > RemoteCatalogRules.MaxEntries)
        {
            throw new RemoteCatalogException("it has too many entries");
        }

        var ignored = 0;
        var skipped = 0;
        var models = new List<CatalogModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in providers)
        {
            var model = Provider(entry ?? throw new RemoteCatalogException("a provider model is empty (null)"));
            Unique(seen, model.Provider + "/" + model.Id);
            if (catalog.FileModels.Any(m => m.Provider == model.Provider && m.Id == model.Id))
            {
                ignored++;
                continue;
            }

            // 0.1.46 review: the file has no music price fields, so a music model from it would be free in every estimate and record. Only an
            // app update adds music models; such an entry is left out, never refused (the rest of the file still counts).
            if (model.Capabilities.HasFlag(ModelCapability.Music))
            {
                skipped++;
                continue;
            }

            models.Add(model);
        }

        var localImages = new List<LocalImageModel>();
        foreach (var entry in images)
        {
            var image = Image(entry ?? throw new RemoteCatalogException("an image model is empty (null)"));
            Unique(seen, ModelProviders.Local + "/" + image.Id);
            if (LocalImageModels.All.Any(m => SameName(m.Id, image.Id))
                || catalog.FileModels.Any(m => m.Provider == ModelProviders.Local && SameName(m.Id, image.Id)))
            {
                ignored++;
                continue;
            }

            if (image.MinSdCppBuild > RemoteCatalogRules.MaxSdCppBuild)
            {
                skipped++;
                continue;
            }

            localImages.Add(image);
            models.Add(new CatalogModel
            {
                Provider = ModelProviders.Local,
                Id = image.Id,
                Name = image.Name,
                Capabilities = ModelCapability.Image,
                Requires = [ComponentCodes.ImagePrefix + image.Id],
                MinVideoMemoryGb = (int)image.Tier,
                License = image.License,
                LicenseUse = image.LicenseUse,
                LicenseUrl = image.LicenseUrl,
            });
        }

        var appModels = new List<AppModelInfo>();
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in ollama)
        {
            var (model, digest) = Ollama(entry ?? throw new RemoteCatalogException("an Ollama model is empty (null)"));
            Unique(seen, ModelProviders.AppOllama + "/" + model.Tag);
            if (AppModelCatalog.Models.Any(m => SameName(m.Tag, model.Tag)))
            {
                ignored++;
                continue;
            }

            appModels.Add(model);
            digests[model.Tag] = digest;
        }

        return new RemoteCatalog(file.Date, models, localImages, appModels, digests) { Ignored = ignored, Skipped = skipped };
    }

    private CatalogModel Provider(RemoteProviderModel entry)
    {
        var where = "provider model " + entry.Provider + "/" + entry.Id;
        var builtIn = catalog.FileModels.Where(m => m.Provider == entry.Provider).ToList();
        if (entry.Provider is ModelProviders.Local or ModelProviders.AppOllama || builtIn.Count == 0)
        {
            throw new RemoteCatalogException(where + ": the provider is not one the app knows");
        }

        // The id goes into request addresses (fal: queue.fal.run/<id>): never an address of its own, a scheme, an empty or dot segment, a query
        // or an escape, so it can only name a model under the provider's own address.
        Require(SafeModelId(entry.Id), where, "the id");
        Require(Text(entry.Name, 120), where, "the name");
        var capabilities = ModelCapability.None;
        Require(CapabilityList().IsMatch(entry.Capabilities) && Enum.TryParse(entry.Capabilities, out capabilities)
            && capabilities != ModelCapability.None, where, "the capabilities");
        var known = builtIn.Aggregate(ModelCapability.None, (all, m) => all | m.Capabilities);
        Require((capabilities & ~known) == ModelCapability.None, where, "a capability this provider's models do not have");
        var efforts = entry.Efforts ?? [];
        Require(efforts.All(e => e is not null && builtIn.Any(m => m.Efforts.Contains(e, StringComparer.Ordinal))), where, "an effort this provider's models do not use");
        Require(entry.ContextTokens is >= 0 and <= 100_000_000 && entry.MaxOutputTokens is >= 0 and <= 10_000_000, where, "the token limits");
        Require(new[] { entry.InputUsd, entry.OutputUsd, entry.CacheReadUsd, entry.ImageUsd, entry.SpeechUsdPerMillionCharacters }.All(p => p is >= 0 and <= 10_000),
            where, "a price");
        Require(entry.RetiresOn.Length == 0 || DateOnly.TryParseExact(entry.RetiresOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            where, "the retirement date");
        return new CatalogModel
        {
            Provider = entry.Provider,
            Id = entry.Id,
            Name = entry.Name,
            Capabilities = capabilities,
            ContextTokens = entry.ContextTokens,
            MaxOutputTokens = entry.MaxOutputTokens,
            Efforts = [.. efforts],
            InputUsd = entry.InputUsd,
            OutputUsd = entry.OutputUsd,
            CacheReadUsd = entry.CacheReadUsd,
            ImageUsd = entry.ImageUsd,
            SpeechUsdPerMillionCharacters = entry.SpeechUsdPerMillionCharacters,
            RetiresOn = entry.RetiresOn,
            PriceSource = PriceSources.BuiltIn,
        };
    }

    private static LocalImageModel Image(RemoteImageModel entry)
    {
        var where = "image model " + entry.Id;
        if (!string.Equals(entry.Engine, RemoteCatalogRules.ImageEngine, StringComparison.Ordinal))
        {
            throw new RemoteCatalogException(where + ": the engine '" + entry.Engine + "' is not one the app has");
        }

        var builtIn = LocalImageModels.All;
        Require(ImageId().IsMatch(entry.Id), where, "the id");
        Require(Text(entry.Name, 120), where, "the name");
        // Only a family a built-in model runs without extra sd-cli options (SD 3.5 and Qwen-Image 2.1 need theirs: an app update adds those).
        Require(builtIn.Any(m => m.Family == entry.Family && m.ExtraArguments.Count == 0), where, "a family the app does not run without extra options");
        Require(entry.Steps is >= 1 and <= 150, where, "the steps");
        Require(entry.CfgScale is >= 0 and <= 30, where, "the guidance scale");
        Require(builtIn.Any(m => m.Sampler == entry.Sampler), where, "a sampler the app does not use");
        Require(entry.Scheduler is null || builtIn.Any(m => m.Scheduler == entry.Scheduler), where, "a scheduler the app does not use");
        Require(entry.FlowShift is null or (>= 0 and <= 30), where, "the flow shift");
        Require(entry.MaxPixels is >= 0 and <= 64_000_000, where, "the largest picture");
        Require(entry.NegativePrompt.Length <= 500, where, "the negative prompt");
        Require(Enum.IsDefined((VramTier)entry.Tier) && entry.Tier > 0, where, "the graphics card class");
        Require(entry.MinSdCppBuild is >= 0 and < 100_000, where, "the stable-diffusion.cpp build");
        var (license, use, url) = License(entry.License, entry.LicenseUse, entry.LicenseUrl, where);
        var files = entry.Files ?? [];
        Require(files.Count is >= 1 and <= 8, where, "the number of files");
        var weights = files.Select(f => File(f ?? throw new RemoteCatalogException(where + ": a file is empty (null)"), where)).ToList();
        Require(weights.Count(w => w.Role is ImageFileRole.Diffusion or ImageFileRole.Full) == 1, where, "exactly one diffusion model or whole checkpoint");
        Require(weights.Select(w => w.File.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == weights.Count, where, "two files with one name");
        return new LocalImageModel(entry.Id, entry.Name, weights)
        {
            Family = entry.Family,
            Steps = entry.Steps,
            CfgScale = entry.CfgScale,
            Sampler = entry.Sampler,
            Scheduler = entry.Scheduler,
            FlowShift = entry.FlowShift,
            MaxPixels = entry.MaxPixels,
            NegativePrompt = entry.NegativePrompt,
            License = license,
            LicenseUse = use,
            LicenseUrl = url,
            Tier = (VramTier)entry.Tier,
            MinSdCppBuild = entry.MinSdCppBuild,
        };
    }

    private static ImageModelFile File(RemoteImageFile file, string where)
    {
        Require(Enum.TryParse<ImageFileRole>(file.Role, out var role) && Enum.IsDefined(role) && Word().IsMatch(file.Role), where, "a file role");
        if (!string.Equals(file.Host, RemoteCatalogRules.ModelHost, StringComparison.Ordinal)
            || !ToolManifest.AllowedHosts.Contains(file.Host, StringComparer.Ordinal))
        {
            throw new RemoteCatalogException(where + ": the host '" + file.Host + "' is not allowed for model files");
        }

        Require(Repository().IsMatch(file.Repository), where, "a repository");
        Require(Commit().IsMatch(file.Commit), where, "a commit (40 lower-case hex characters)");
        Require(RepositoryPath().IsMatch(file.Path) && !file.Path.Split('/').Any(s => s is "." or ".."), where, "a file path");
        if (!Sha256().IsMatch(file.Sha256))
        {
            throw new RemoteCatalogException(where + ": " + file.Path + " has no SHA-256 (64 lower-case hex characters)");
        }

        Require(file.Bytes is > 0 and <= RemoteCatalogRules.MaxModelBytes, where, "a file size");
        var archive = HuggingFaceFile.Pinned(file.Repository, file.Commit, file.Path, file.Sha256, file.Bytes);
        Require(archive.Url.Scheme == Uri.UriSchemeHttps && archive.Url.Host == RemoteCatalogRules.ModelHost, where, "a file address");
        return new ImageModelFile(role, archive);
    }

    private static (AppModelInfo Model, string Digest) Ollama(RemoteOllamaModel entry)
    {
        var where = "Ollama model " + entry.Tag;
        Require(OllamaTag().IsMatch(entry.Tag) && AppModelStore.IsValidTag(entry.Tag) && !entry.Tag.EndsWith(":latest", StringComparison.Ordinal), where,
            "an exact tag");
        Require(Text(entry.Name, 120), where, "the name");
        Require(OllamaFamily().IsMatch(entry.Family), where, "the family");
        Require(entry.Bytes is > 0 and <= RemoteCatalogRules.MaxModelBytes, where, "the size");
        Require(entry.ContextTokens is >= 512 && entry.TrainedContextTokens >= entry.ContextTokens && entry.TrainedContextTokens <= 100_000_000, where,
            "the context");
        Require(entry.KvBytesPerToken is > 0 and <= 16 << 20, where, "the cache size per token");
        Require(entry.SourceUrl.StartsWith(RemoteCatalogRules.OllamaLibrary, StringComparison.Ordinal) && Uri.IsWellFormedUriString(entry.SourceUrl, UriKind.Absolute),
            where, "the library page");
        if (!Digest().IsMatch(entry.ManifestDigest))
        {
            throw new RemoteCatalogException(where + ": no manifest digest (sha256: and 64 lower-case hex characters)");
        }

        var (license, use, url) = License(entry.License, entry.LicenseUse, entry.LicenseUrl, where);
        var model = new AppModelInfo(entry.Tag, entry.Name, entry.Family, entry.Bytes, entry.ContextTokens, entry.TrainedContextTokens, entry.KvBytesPerToken,
            entry.Vision, entry.CanThink, license, use, url, entry.SourceUrl);
        return (model, entry.ManifestDigest);
    }

    private static (string License, LicenseUse Use, string Url) License(string license, string use, string url, string where)
    {
        Require(LicenseCode().IsMatch(license), where, "the licence code");
        var kind = LicenseUse.Commercial;
        Require(Word().IsMatch(use) && Enum.TryParse(use, out kind) && Enum.IsDefined(kind), where, "what the licence allows");
        var https = Uri.TryCreate(url, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps;
        Require(url.Length == 0 ? kind == LicenseUse.Commercial : https, where, "the licence address");
        return (license, kind, url);
    }

    private static void Unique(HashSet<string> seen, string key)
    {
        if (!seen.Add(key))
        {
            throw new RemoteCatalogException(key + " is listed twice");
        }
    }

    /// <summary>
    /// A display name: not blank, not too long, and only characters that show as themselves (no control, no format characters such as the
    /// right-to-left override or a zero-width space, no private-use, unpaired surrogate or unassigned code points), so a name cannot pose as
    /// another one in the pickers.
    /// </summary>
    private static bool Text(string value, int max) => value.Trim().Length > 0 && value.Length <= max && value.All(Visible);

    private static bool Visible(char c) => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Format or UnicodeCategory.Surrogate
        or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned);

    /// <summary>
    /// A cloud model id: the id pattern, no address of its own (<c>https://…</c>, <c>x:y</c>), no empty, <c>.</c> or <c>..</c> segment; a colon
    /// only as OpenRouter's <c>:free</c> suffix.
    /// </summary>
    internal static bool SafeModelId(string id)
    {
        if (!ModelId().IsMatch(id) || Uri.TryCreate(id, UriKind.Absolute, out _) || id.Split('/').Any(s => s is "" or "." or ".."))
        {
            return false;
        }

        return !id.Contains(':', StringComparison.Ordinal) || OpenRouterSuffix().IsMatch(id);
    }

    /// <summary>One name on disk: Windows ignores letter case and trailing dots and spaces.</summary>
    private static bool SameName(string a, string b) => string.Equals(a.TrimEnd('.', ' '), b.TrimEnd('.', ' '), StringComparison.OrdinalIgnoreCase);

    private static void Require(bool ok, string where, string what)
    {
        if (!ok)
        {
            throw new RemoteCatalogException(where + ": " + what + " is not valid");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,127}$")]
    private static partial Regex ModelId();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/@-]*:free$")]
    private static partial Regex OpenRouterSuffix();

    // The id is a folder name (tools/images/<id>): it ends in a letter or digit (Windows drops a trailing dot, so "z-image-turbo." would be
    // the built-in model's folder) and has no "..".
    [GeneratedRegex(@"^(?!.*\.\.)[a-z0-9][a-z0-9.-]{0,62}[a-z0-9]$")]
    private static partial Regex ImageId();

    [GeneratedRegex(@"^[A-Za-z]+(, ?[A-Za-z]+)*$")]
    private static partial Regex CapabilityList();

    [GeneratedRegex(@"^[A-Za-z]+$")]
    private static partial Regex Word();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,95}/[A-Za-z0-9][A-Za-z0-9._-]{0,95}$")]
    private static partial Regex Repository();

    [GeneratedRegex(@"^[0-9a-f]{40}$")]
    private static partial Regex Commit();

    // Every segment ends in a letter, digit, '_' or '-' (a trailing dot is dropped on Windows: "a.gguf." would be "a.gguf").
    [GeneratedRegex(@"^[A-Za-z0-9._-]*[A-Za-z0-9_-](/[A-Za-z0-9._-]*[A-Za-z0-9_-]){0,7}$")]
    private static partial Regex RepositoryPath();

    [GeneratedRegex(@"^[0-9a-f]{64}$")]
    private static partial Regex Sha256();

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$")]
    private static partial Regex Digest();

    // The tag is a manifest path (manifests/…/<name>/<tag>): both parts end in a letter or digit, like the folder names above.
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?:[A-Za-z0-9](?:[A-Za-z0-9_.-]{0,62}[A-Za-z0-9])?$")]
    private static partial Regex OllamaTag();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex OllamaFamily();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.+-]{0,63}$")]
    private static partial Regex LicenseCode();
}
