using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Core.Keys;
using TMConcept.Core.Providers;
using TMConcept.Core.Speech;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Speech;

/// <summary>
/// The voices of the user's ElevenLabs account (0.1.47, H4): <c>GET v2/voices?page_size=100</c> page by page with
/// <c>next_page_token</c> while <c>has_more</c> (the same request as the free key test; the API reference read on 24 Nov 2026). Premade,
/// cloned and library voices added to "My Voices" all come back. A voice's languages are its <c>verified_languages[].language</c>, else
/// its <c>labels.language</c>; none = unknown (<see cref="AccountVoiceFilter"/>). At most <see cref="MaxPages"/> pages are read.
/// </summary>
public sealed class ElevenLabsVoiceSource(ElevenLabsProvider provider, ProviderHttp http) : IAccountVoiceSource
{
    /// <summary>Voices per page (the largest the API allows).</summary>
    public const int PageSize = 100;

    /// <summary>The most pages read (10 000 voices); a longer account is cut there.</summary>
    public const int MaxPages = 100;

    /// <inheritdoc />
    public string Provider => provider.Code;

    /// <summary>The address of one page.</summary>
    public Uri PageAddress(string? token) => new(provider.Address, "../v2/voices?page_size=" + PageSize.ToString(CultureInfo.InvariantCulture)
        + (string.IsNullOrEmpty(token) ? string.Empty : "&next_page_token=" + Uri.EscapeDataString(token)));

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountVoice>> FetchAsync(ApiKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var all = new List<AccountVoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var address = PageAddress(token);
            var reply = await http.SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, address);
                provider.Authorize(request, key.Secret);
                return request;
            }, provider.Name, key.Secret, cancellationToken).ConfigureAwait(false);
            var (voices, next) = ParsePage(reply.Body);
            all.AddRange(voices.Where(v => seen.Add(v.Id)));
            if (next is null || next == token)
            {
                return all;
            }

            token = next;
        }

        return all;
    }

    /// <summary>One page of the answer: its voices and the token of the next page (null when there is none).</summary>
    public static (IReadOnlyList<AccountVoice> Voices, string? Next) ParsePage(byte[] body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ProviderException("ElevenLabs returned a voice list that could not be read.", ex) { Code = "http" };
        }

        if (root?["voices"] is not JsonArray items)
        {
            throw new ProviderException("ElevenLabs returned no voice list.") { Code = "http" };
        }

        var voices = new List<AccountVoice>();
        foreach (var item in items.OfType<JsonObject>())
        {
            var id = Text(item["voice_id"]);

            // The id goes into the speech address: only the plain letters and digits ElevenLabs uses.
            if (id.Length == 0 || !id.All(char.IsAsciiLetterOrDigit))
            {
                continue;
            }

            var name = Text(item["name"]);
            voices.Add(new AccountVoice(id, name.Length > 0 ? name : id, Text(item["category"]), Languages(item)));
        }

        var more = root["has_more"] is JsonValue flag && flag.TryGetValue<bool>(out var value) && value;
        var next = Text(root["next_page_token"]);
        return (voices, more && next.Length > 0 ? next : null);
    }

    private static List<string> Languages(JsonObject voice)
    {
        var languages = new List<string>();
        if (voice["verified_languages"] is JsonArray verified)
        {
            foreach (var entry in verified.OfType<JsonObject>())
            {
                var code = AccountVoiceFilter.Code(Text(entry["language"]));
                if (code.Length > 0 && !languages.Contains(code))
                {
                    languages.Add(code);
                }
            }
        }

        if (languages.Count == 0 && AccountVoiceFilter.Code(Text(voice["labels"]?["language"])) is { Length: > 0 } label)
        {
            languages.Add(label);
        }

        return languages;
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;
}
