using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Paths;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Updates;
using TMConcept.Engines.Images;
using TMConcept.Providers.Catalog;

namespace TMConcept.Desktop.Catalog;

/// <summary>
/// The remote model catalog (0.1.26, the developer: models can be added from a JSON file in the repository, like the prices): at start the
/// last good copy (<c>&lt;data&gt;/catalog/repository.json</c>) is checked again and its models are added before anything reads the model
/// lists (<see cref="ApplySaved"/>, no network); in the background, at most once a day, the repository file is read, checked
/// (<see cref="RemoteCatalogReader"/>) and kept as the new copy, whose models appear at the next start. A refused or unreachable file is one
/// log line and changes nothing: the built-in catalog is the fallback and the floor (nothing built in is removed or changed). A model a
/// later file no longer lists stays in the kept copy while it is installed here, so it keeps working.
/// </summary>
public sealed class RemoteCatalogUpdater(RemoteCatalogReader reader, EmbeddedModelCatalog catalog, IAppPaths paths, UpdateTransport transport,
    LocalImageStore images, AppModelStore ollama, TimeProvider clock, ILogger<RemoteCatalogUpdater> logger)
{
    private const string SavedName = "repository.json";
    private const string CheckedName = "checked.txt";

    /// <summary>The running version (a file whose <c>minApp</c> is newer is refused).</summary>
    public string Current { get; init; } = AppIdentity.ShortVersion;

    /// <summary>The folder of the kept copy.</summary>
    public string Folder => Path.Combine(paths.DataDirectory, "catalog");

    /// <summary>
    /// Adds the models of the kept copy (checked again: a copy from an older app, or a damaged one, is not used); null when there is none or
    /// it was refused. Called once at start, before the model lists are read; never throws for the copy.
    /// </summary>
    public RemoteCatalog? ApplySaved()
    {
        var file = Path.Combine(Folder, SavedName);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var remote = reader.Read(File.ReadAllText(file), Current);
            var added = Apply(remote);
            logger.LogInformation("Model catalog of {Date}: {Added} model(s) added ({Ignored} built in already, {Skipped} need a newer app)", remote.Date, added,
                remote.Ignored, remote.Skipped);
            return remote;
        }
        catch (RemoteCatalogException ex)
        {
            logger.LogWarning("The kept model catalog is not used ({Reason}); the built-in catalog stays", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The kept model catalog could not be read; the built-in catalog stays");
        }

        return null;
    }

    /// <summary>
    /// Reads the repository file when the last read is a day old or more (<paramref name="force"/> skips the wait); returns whether it was read.
    /// Never throws for a network, file or content problem: it is logged and the kept copy stays.
    /// </summary>
    public async Task<bool> UpdateAsync(bool force, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var checkedFile = Path.Combine(Folder, CheckedName);
        if (!force && LastCheck(checkedFile) is { } last && now - last < RemoteCatalogRules.Interval && now >= last)
        {
            return false;
        }

        string json;
        try
        {
            json = await FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
            or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            logger.LogWarning("The model catalog was not read from the repository ({Reason}); the kept copy stays", ex.Message);
            return false;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            Save(json);
            await File.WriteAllTextAsync(checkedFile, now.ToString("O", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The model catalog could not be kept; it is read again at the next start");
        }

        return true;
    }

    /// <summary>Adds a checked catalog's models to the app's lists; returns how many were new.</summary>
    internal int Apply(RemoteCatalog remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var added = LocalImageModelSet.Add(remote.Images);
        added += AppModelSet.Add(remote.Ollama, remote.Digests);
        added += catalog.AddModels(remote.Models.Where(m => m.Provider != Core.Models.ModelProviders.Local).ToList());
        catalog.AddModels(remote.Models.Where(m => m.Provider == Core.Models.ModelProviders.Local).ToList());
        return added;
    }

    /// <summary>Checks the downloaded file and keeps it (with the installed models a previous copy had and this one dropped).</summary>
    private void Save(string json)
    {
        RemoteCatalog remote;
        try
        {
            remote = reader.Read(json, Current);
        }
        catch (RemoteCatalogException ex)
        {
            logger.LogWarning("The model catalog from the repository was refused ({Reason}); the kept copy stays", ex.Message);
            return;
        }

        var target = Path.Combine(Folder, SavedName);
        var kept = KeepInstalled(json, target);
        File.WriteAllText(target + ".new", kept, new UTF8Encoding(false));
        File.Move(target + ".new", target, overwrite: true);
        logger.LogInformation("Model catalog of {Date} kept: {Count} model(s) beyond the built-in ones; new ones appear at the next start", remote.Date, remote.Count);
    }

    /// <summary>
    /// The new file, plus every image and Ollama model of the previous copy that it no longer lists and that is installed here (so it stays
    /// usable and removable). Falls back to the new file when the combination is not a valid catalog.
    /// </summary>
    private string KeepInstalled(string json, string previousFile)
    {
        if (!File.Exists(previousFile))
        {
            return json;
        }

        try
        {
            var previousJson = File.ReadAllText(previousFile);
            var previous = reader.Read(previousJson, Current);
            var installedImages = previous.Images.Where(m => images.Missing(m).Count < m.Files.Count).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
            var installedOllama = previous.Ollama.Where(m => ollama.HasManifest(m.Tag)).Select(m => m.Tag).ToHashSet(StringComparer.Ordinal);
            if (installedImages.Count == 0 && installedOllama.Count == 0)
            {
                return json;
            }

            var next = JsonSerializer.Deserialize(json, RemoteCatalogJsonContext.Default.RemoteCatalogFile)!;
            var old = JsonSerializer.Deserialize(previousJson, RemoteCatalogJsonContext.Default.RemoteCatalogFile)!;
            next.Images ??= [];
            next.Ollama ??= [];
            var keptImages = (old.Images ?? []).Where(m => installedImages.Contains(m.Id) && next.Images.All(n => n.Id != m.Id)).ToList();
            var keptOllama = (old.Ollama ?? []).Where(m => installedOllama.Contains(m.Tag) && next.Ollama.All(n => n.Tag != m.Tag)).ToList();
            if (keptImages.Count == 0 && keptOllama.Count == 0)
            {
                return json;
            }

            next.Images.AddRange(keptImages);
            next.Ollama.AddRange(keptOllama);
            var combined = JsonSerializer.Serialize(next, RemoteCatalogJsonContext.Default.RemoteCatalogFile);
            reader.Read(combined, Current);
            logger.LogInformation("Model catalog: {Count} installed model(s) the new file no longer lists are kept ({Ids})", keptImages.Count + keptOllama.Count,
                string.Join(", ", keptImages.Select(m => m.Id).Concat(keptOllama.Select(m => m.Tag))));
            return combined;
        }
        catch (Exception ex) when (ex is RemoteCatalogException or JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The installed models of the previous model catalog could not be carried over ({Reason}); the new file is kept as it is", ex.Message);
            return json;
        }
    }

    private async Task<string> FetchAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = RemoteCatalogRules.RequestTimeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, RemoteCatalogRules.RepositoryFile);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UpdateRules.UserAgent, AppIdentity.ShortVersion));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri is { } final && final != RemoteCatalogRules.RepositoryFile)
        {
            throw new InvalidDataException("the answer came from another address (" + final.Host + ")");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > RemoteCatalogRules.MaxFileBytes)
            {
                throw new InvalidDataException("the file is larger than " + RemoteCatalogRules.MaxFileBytes.ToString(CultureInfo.InvariantCulture) + " bytes");
            }
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private DateTimeOffset? LastCheck(string file)
    {
        try
        {
            return File.Exists(file) && DateTimeOffset.TryParse(File.ReadAllText(file).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var at) ? at : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The time of the last model catalog read could not be read");
            return null;
        }
    }
}
