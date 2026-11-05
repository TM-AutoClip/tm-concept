using System.Text.Json.Nodes;

namespace TMConcept.Providers.Common;

/// <summary>Tolerant readers for provider JSON (a missing or wrongly typed field is 0 / empty, never an exception).</summary>
internal static class JsonRead
{
    /// <summary>A number.</summary>
    public static long Long(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out var l))
            {
                return l;
            }

            if (value.TryGetValue<double>(out var d))
            {
                return (long)d;
            }
        }

        return 0;
    }

    /// <summary>A decimal number; <c>null</c> when missing.</summary>
    public static decimal? Decimal(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var d) ? (decimal)d : null;

    /// <summary>A string.</summary>
    public static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : string.Empty;
}
