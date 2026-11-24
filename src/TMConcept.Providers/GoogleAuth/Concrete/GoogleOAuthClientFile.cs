using System.Text.Json;
using System.Text.Json.Nodes;

namespace TMConcept.Providers.GoogleAuth;

/// <summary>
/// Reads the client file Google downloads for an OAuth client (Google Auth Platform → Clients → the download button). Only a "Desktop app"
/// client works with the loopback sign-in: its file holds <c>{"installed": {"client_id", "client_secret", …}}</c>. A "Web application"
/// client (<c>"web"</c>), another JSON (a service account key) or a damaged file is refused with a code the page turns into a clear text.
/// </summary>
public static class GoogleOAuthClientFile
{
    /// <summary>The largest file read (a client file is under 1 KB).</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>Reads the client of a client file's text.</summary>
    public static GoogleOAuthClient Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json ?? string.Empty);
        }
        catch (JsonException ex)
        {
            throw new GoogleSignInException("The client file is not a JSON file.", ex) { Code = "file.json" };
        }

        if (root is not JsonObject top)
        {
            throw new GoogleSignInException("The client file is not a JSON object.") { Code = "file.json" };
        }

        if (top["installed"] is not JsonObject installed)
        {
            throw top.ContainsKey("web")
                ? new GoogleSignInException("The client file is of a web application client; a desktop app client is needed.") { Code = "file.web" }
                : new GoogleSignInException("The file is not the file of an OAuth client.") { Code = "file.kind" };
        }

        var id = Text(installed["client_id"]);
        var secret = Text(installed["client_secret"]);
        if (id.Length == 0 || secret.Length == 0)
        {
            throw new GoogleSignInException("The client file has no client id or client secret.") { Code = "file.fields" };
        }

        return new GoogleOAuthClient(id, secret);
    }

    /// <summary>Reads a client file from disk (off the window's thread).</summary>
    public static async Task<GoogleOAuthClient> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is 0 or > MaxBytes)
        {
            throw new GoogleSignInException("The client file is empty or too large.") { Code = "file.kind" };
        }

        return Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;
}
