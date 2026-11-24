namespace TMConcept.Core.Channels;

/// <summary>The one call of "Kanaldan esinlen" (<see cref="ChannelPromptBuilder.Build"/>): the system part and the user part.</summary>
public sealed record ChannelPrompt(string System, string User);
