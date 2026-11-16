using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The latest release of the project on GitHub (<see cref="UpdateRules.LatestRelease"/>), read without a token and kept in
/// <c>update-check.json</c>: the start looks at most once a day (<see cref="UpdateRules.CheckEvery"/>); a look sends GitHub's last ETag, so an
/// unchanged release costs nothing of the hourly limit; a rate-limit answer (403 / 429 with no requests left, or Retry-After) is respected
/// until the time GitHub names, also by "Check now". Offline or refused: the last known release is used and the reason is logged.
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

    /// <summary>
    /// The latest release: the kept one when the last look is younger than a day (unless <paramref name="force"/>) or GitHub asked to wait,
    /// otherwise GitHub's answer. Null when none is known. Network problems are logged and leave the kept one.
    /// </summary>
    public async Task<GitHubRelease?> LatestAsync(bool force, CancellationToken cancellationToken)
    {
        var state = State();
        var now = _clock.GetUtcNow();
        if (state.NotBeforeUtc is { } notBefore && now < notBefore)
        {
            _logger.LogInformation("Update check: GitHub asked to wait until {Time:u}; the last known release is used", notBefore);
            return state.Release;
        }

        if (!force && state.CheckedAtUtc is { } last && now - last < UpdateRules.CheckEvery && now >= last)
        {
            return state.Release;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateRules.LatestRelease);
        request.Headers.UserAgent.ParseAdd(UpdateRules.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", UpdateRules.ApiVersion);
        if (state.ETag is { Length: > 0 } etag && state.Release is not null && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                state.CheckedAtUtc = now;
                state.NotBeforeUtc = null;
                Save(state);
                _logger.LogInformation("Update check: the latest release is unchanged ({Tag})", state.Release?.Tag);
                return state.Release;
            }

            if (Limited(response, now) is { } wait)
            {
                state.NotBeforeUtc = wait;
                Save(state);
                _logger.LogWarning("Update check: GitHub's limit for requests without a token is reached; next look after {Time:u}", wait);
                return state.Release;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No release published yet.
                state.CheckedAtUtc = now;
                state.Release = null;
                state.ETag = null;
                Save(state);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Update check: GitHub answered HTTP {Status}; the last known release is used", (int)response.StatusCode);
                return state.Release;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize(body, DesktopJsonContext.Default.GitHubRelease);
            state.CheckedAtUtc = now;
            state.NotBeforeUtc = null;
            state.ETag = response.Headers.ETag?.ToString();
            state.Release = release is { Draft: false, Prerelease: false } ? release : null;
            Save(state);
            _logger.LogInformation("Update check: the latest release is {Tag}", state.Release?.Tag ?? "(none)");
            return state.Release;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException
            or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            _logger.LogWarning(ex, "Update check: GitHub could not be reached; the last known release is used");
            return state.Release;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

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
