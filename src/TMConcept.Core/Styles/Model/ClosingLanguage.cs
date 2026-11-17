namespace TMConcept.Core.Styles;

/// <summary>The closing sentences of one video language and the button text of the end card.</summary>
/// <param name="Code">Language code (<see cref="Profiles.ContentLanguages"/>).</param>
/// <param name="SubscribeLabel">End card button with "subscribe" ("Abone ol").</param>
/// <param name="FollowLabel">End card button with "follow" ("Takip et").</param>
/// <param name="Lines">One line per <see cref="ClosingPhrase"/>.</param>
public sealed record ClosingLanguage(string Code, string SubscribeLabel, string FollowLabel, IReadOnlyList<ClosingLine> Lines);
