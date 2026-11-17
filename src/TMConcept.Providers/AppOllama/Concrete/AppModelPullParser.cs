using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Core.AppOllama;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.AppOllama;

/// <summary>
/// Reads one line of Ollama's pull stream (<c>{"status":"pulling 6a0746a1ec1a","digest":"sha256:…","total":2019377376,"completed":241970}</c>,
/// <c>{"status":"success"}</c>, <c>{"error":"…"}</c>). A line that is not a JSON object is no progress (null).
/// </summary>
public static class AppModelPullParser
{
    /// <summary>The progress in <paramref name="line"/>; null when it is empty or not a JSON object.</summary>
    public static AppModelPullProgress? Read(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        if (node is not JsonObject item)
        {
            return null;
        }

        return new AppModelPullProgress(JsonRead.Text(item["status"]), JsonRead.Text(item["digest"]), Math.Max(0, JsonRead.Long(item["total"])),
            Math.Max(0, JsonRead.Long(item["completed"])), JsonRead.Text(item["error"]));
    }
}
