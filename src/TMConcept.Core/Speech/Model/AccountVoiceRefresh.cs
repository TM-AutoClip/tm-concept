namespace TMConcept.Core.Speech;

/// <summary>
/// The result of reading a provider's account voices again: <see cref="Ok"/> with the number of voices, or the reason it failed
/// (<c>key</c>: no API key; a <see cref="Providers.ProviderException"/> code: <c>auth</c>, <c>network</c>…; <c>busy</c>: another read runs).
/// </summary>
public sealed record AccountVoiceRefresh(bool Ok, int Count, string Code);
