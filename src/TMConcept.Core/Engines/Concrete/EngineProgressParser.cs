using System.Globalization;
using System.Text.Json;

namespace TMConcept.Core.Engines;

/// <summary>
/// Reads progress from engine output. The app's render scripts (Remotion, Revideo) write one JSON object per line:
/// <c>{"event":"bundle"|"progress"|"done"|"error"|"cancel", …}</c>; the HyperFrames command line writes
/// <c>@hf-progress {"code","pct",…}</c>. Other lines (the engines' own logs) are ignored. The text is untrusted: only numbers and a short
/// message are taken.
/// </summary>
public static class EngineProgressParser
{
    /// <summary>Prefix of HyperFrames progress lines.</summary>
    public const string HyperFramesPrefix = "@hf-progress ";

    /// <summary>The event of a line; <c>null</c> when the line is not an event.</summary>
    public static EngineEvent? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var text = line.Trim();
        try
        {
            if (text.StartsWith(HyperFramesPrefix, StringComparison.Ordinal))
            {
                return HyperFrames(text[HyperFramesPrefix.Length..]);
            }

            return text.StartsWith('{') ? Script(text) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EngineEvent HyperFrames(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var percent = root.TryGetProperty("pct", out var p) && p.TryGetDouble(out var v) ? v : 0;
        var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty;
        var name = code switch
        {
            "capture" => "capturing frames",
            "encode" => "encoding",
            "assemble" => "assembling",
            _ => Short(code),
        };
        var counts = root.TryGetProperty("done", out var done) && root.TryGetProperty("total", out var total) && done.TryGetInt32(out var a)
            && total.TryGetInt32(out var b)
            ? string.Create(CultureInfo.InvariantCulture, $" {a}/{b}")
            : string.Empty;
        return new EngineEvent(Math.Clamp(percent / 100.0, 0, 1), name + counts);
    }

    private static EngineEvent? Script(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var kind) || kind.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string Text(string name) => root.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String ? Short(x.GetString()) : string.Empty;
        double Number(string name) => root.TryGetProperty(name, out var x) && x.TryGetDouble(out var d) ? d : 0;
        switch (kind.GetString())
        {
            case "bundle":
                var percent = Number("percent");
                return new EngineEvent(Math.Clamp(percent / 100.0, 0, 1) * 0.1, string.Create(CultureInfo.InvariantCulture, $"bundling {percent:0} %"));
            case "progress":
                var ratio = Math.Clamp(Number("ratio"), 0, 1);
                var message = Text("message");
                return new EngineEvent(0.1 + (0.9 * ratio), message.Length > 0 ? message : string.Create(CultureInfo.InvariantCulture, $"{ratio * 100:0} %"));
            case "done":
                return new EngineEvent(1, "done", Done: true);
            case "error":
                var error = Text("message");
                return new EngineEvent(null, "error", Error: error.Length > 0 ? error : "unknown error");
            case "cancel":
                return new EngineEvent(null, "canceled", Canceled: true);
            default:
                return null;
        }
    }

    private static string Short(string? text)
    {
        var value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 300 ? value : value[..300] + "…";
    }
}
