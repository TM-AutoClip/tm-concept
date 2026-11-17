using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The project's published releases on GitHub (<see cref="UpdateRules.Releases"/>, every page up to <see cref="UpdateRules.MaxPages"/>), read
/// without a token and kept in <c>update-check.json</c>: the start looks at most once a day (<see cref="UpdateRules.CheckEvery"/>); a look
/// sends GitHub's last ETag of the first page, so an unchanged list costs nothing of the hourly limit; a rate-limit answer (403 / 429 with no
/// requests left, or Retry-After) is respected until the time GitHub names, also by "Check now". Offline or refused: the last known list is
/// used and the reason is logged. Drafts and pre-releases are never kept.
/// </summary>
public sealed class ReleaseFeed : IDisposable
{
    private readonly AppPaths _paths;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReleaseFeed> _logger;
    private readonly HttpClient _http;

    /// <summary>Creates.</summary>
    public ReleaseFeed(UpdateTransport transport, AppPaths paths, TimeProvider clock, ILogger<ReleaseFeed> logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _paths = paths;
        _clock = clock;
        _logger = logger;
        _http = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = UpdateRules.RequestTimeout };
    }

    /// <summary>The last look (never null; empty before the first).</summary>
    public UpdateCheckState State()
    {
        if (!File.Exists(_paths.UpdateCheckFile))
        {
            return new UpdateCheckState();
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(_paths.UpdateCheckFile), DesktopJsonContext.Default.UpdateCheckState) ?? new UpdateCheckState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The last update check could not be read; it starts again");
            return new UpdateCheckState();
        }
    }

    /// <summary>The newest published release (by version), or null when none is known (<see cref="ReleasesAsync"/>).</summary>
    public async Task<GitHubRelease?> LatestAsync(bool force, CancellationToken cancellationToken) =>
        (await ReleasesAsync(force, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    /// <summary>
    /// The published releases, newest version first: the kept list when the last look is younger than a day (unless <paramref name="force"/>)
    /// or GitHub asked to wait, otherwise GitHub's answer. Empty when none is known. Network problems are logged and leave the kept list.
    /// </summary>
    public async Task<IReadOnlyList<GitHubRelease>> ReleasesAsync(bool force, CancellationToken cancellationToken)
    {
        var state = State();
        var now = _clock.GetUtcNow();
        var kept = state.Releases ?? [];
        if (state.NotBeforeUtc is { } notBefore && now < notBefore)
        {
            _logger.LogInformation("Update check: GitHub asked to wait until {Time:u}; the last known releases are used", notBefore);
            return kept;
        }

        if (!force && state.Releases is not null && state.CheckedAtUtc is { } last && now - last < UpdateRules.CheckEvery && now >= last)
        {
            return kept;
        }

        var found = new List<GitHubRelease>();
        string? etag = null;
        var complete = false;
        Uri? page = UpdateRules.Releases;
        try
        {
            for (var number = 1; page is not null && number <= UpdateRules.MaxPages; number++)
            {
                using var request = Request(page, number == 1 && state.Releases is { Count: > 0 } ? state.ETag : null);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (number == 1 && response.StatusCode == HttpStatusCode.NotModified)
                {
                    state.CheckedAtUtc = now;
                    state.NotBeforeUtc = null;
                    Save(state);
                    _logger.LogInformation("Update check: the release list is unchanged (newest {Tag})", kept.FirstOrDefault()?.Tag ?? "(none)");
                    return kept;
                }

                if (Limited(response, now) is { } wait)
                {
                    state.NotBeforeUtc = wait;
                    _logger.LogWarning("Update check: GitHub's limit for requests without a token is reached; next look after {Time:u}", wait);
                    break;
                }

                if (number == 1 && response.StatusCode == HttpStatusCode.NotFound)
                {
                    // No release list (the project is not published yet).
                    state.CheckedAtUtc = now;
                    state.Releases = [];
                    state.ETag = null;
                    Save(state);
                    return [];
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Update check: GitHub answered HTTP {Status} for page {Page} of the release list", (int)response.StatusCode, number);
                    break;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                found.AddRange(JsonSerializer.Deserialize(body, DesktopJsonContext.Default.ListGitHubRelease) ?? []);
                if (number == 1)
                {
                    etag = response.Headers.ETag?.ToString();
                }

                page = Next(response);
                complete = page is null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException
            or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            _logger.LogWarning(ex, "Update check: GitHub could not be reached; the last known releases are used");
            return kept;
        }

        if (found.Count == 0 && !complete)
        {
            // Nothing was read (refused at the first page): the kept list stays, and so does the time of the last full look.
            Save(state);
            return kept;
        }

        var published = Published(found);
        state.CheckedAtUtc = now;
        state.NotBeforeUtc = complete ? null : state.NotBeforeUtc;

        // A list read only in part (a later page refused) keeps no ETag: the next look reads it again instead of trusting a 304.
        state.ETag = complete ? etag : null;
        state.Releases = published;
        Save(state);
        _logger.LogInformation("Update check: {Count} published release(s), the newest is {Tag}{Part}", published.Count,
            published.FirstOrDefault()?.Tag ?? "(none)", complete ? string.Empty : " (the list was read only in part)");
        return published;
    }

    /// <summary>The releases that are published (no draft, no pre-release) with a version as their tag, newest version first.</summary>
    public static List<GitHubRelease> Published(IEnumerable<GitHubRelease> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);
        return releases
            .Where(r => r is { Draft: false, Prerelease: false })
            .Select(r => (Release: r, Ok: AppVersion.TryParse(r.Tag, out var version), Version: version))
            .Where(r => r.Ok && string.IsNullOrEmpty(r.Version.Prerelease))
            .GroupBy(r => r.Version)
            .Select(g => g.First())
            .OrderByDescending(r => r.Version)
            .Select(r => r.Release)
            .ToList();
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static HttpRequestMessage Request(Uri address, string? etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd(UpdateRules.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", UpdateRules.ApiVersion);
        if (etag is { Length: > 0 } && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        return request;
    }

    /// <summary>
    /// The next page of the list from GitHub's Link header (<c>&lt;…&gt;; rel="next"</c>); null on the last page or for an address that is
    /// not GitHub's API.
    /// </summary>
    internal static Uri? Next(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var part in values.SelectMany(v => v.Split(',')))
        {
            var pieces = part.Split(';', StringSplitOptions.TrimEntries);
            if (pieces.Length < 2 || !pieces.Skip(1).Any(p => p.Replace(" ", string.Empty, StringComparison.Ordinal) == "rel=\"next\""))
            {
                continue;
            }

            var target = pieces[0].Trim('<', '>');
            return target.StartsWith(UpdateRules.ApiPrefix, StringComparison.Ordinal) && Uri.TryCreate(target, UriKind.Absolute, out var next) ? next : null;
        }

        return null;
    }

    /// <summary>When a rate-limit answer allows the next request; null when the answer is not one.</summary>
    private static DateTimeOffset? Limited(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return null;
        }

        if (response.Headers.RetryAfter is { } retry)
        {
            return retry.Date ?? now + (retry.Delta ?? TimeSpan.FromMinutes(1));
        }

        var remaining = Header(response, "X-RateLimit-Remaining");
        if (remaining == "0" && long.TryParse(Header(response, "X-RateLimit-Reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var reset))
        {
            return DateTimeOffset.FromUnixTimeSeconds(reset);
        }

        // A 403/429 without the headers is still a refusal: wait an hour rather than ask again at once.
        return response.StatusCode == HttpStatusCode.TooManyRequests || remaining == "0" ? now + TimeSpan.FromHours(1) : null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private void Save(UpdateCheckState state)
    {
        try
        {
            var temporary = _paths.UpdateCheckFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, DesktopJsonContext.Default.UpdateCheckState));
            File.Move(temporary, _paths.UpdateCheckFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The update check could not be kept; the next start looks again");
        }
    }
}
