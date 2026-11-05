using System.Text.Json.Nodes;

namespace TMConcept.Core.Chat;

/// <summary>
/// One chat call. <see cref="System"/> is the stable part (cached when <see cref="CacheSystem"/>: put the long, unchanging text there);
/// <see cref="User"/> is the changing part. <see cref="Schema"/> asks for an answer that follows a JSON schema.
/// </summary>
public sealed record ChatRequest(string System, string User, bool CacheSystem = false, JsonObject? Schema = null, int MaxOutputTokens = 16000,
    string Effort = "", IReadOnlyList<ImagePart>? Images = null);
