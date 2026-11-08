using System.Text.Json.Nodes;

namespace TMConcept.Core.Chat;

/// <summary>
/// One chat call. <see cref="System"/> is the stable part (cached when <see cref="CacheSystem"/>: put the long, unchanging text there);
/// <see cref="User"/> is the changing part. <see cref="Schema"/> asks for an answer that follows a JSON schema. <see cref="Stream"/> marks a
/// call whose answer may take minutes (concepts, video code): it is read while it streams, so no single time limit cuts it (Anthropic
/// always streams; the OpenAI-compatible providers stream when this is set). <see cref="Progress"/> (optional) gets the call's steps for the
/// screen: sent, thinking, writing, done (<see cref="ChatProgress"/>).
/// </summary>
public sealed record ChatRequest(string System, string User, bool CacheSystem = false, JsonObject? Schema = null, int MaxOutputTokens = 16000,
    string Effort = "", IReadOnlyList<ImagePart>? Images = null, bool Stream = false, IProgress<ChatProgress>? Progress = null);
