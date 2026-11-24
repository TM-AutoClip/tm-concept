using TMConcept.Core.References;

namespace TMConcept.Core.Channels;

/// <summary>
/// The result of looking at a channel (<see cref="IChannelInspirer.InspectAsync"/>): its public details, the facts the numbers give, the
/// merged tempo profile of the measured samples (null when none could be measured), how many samples were measured and how many failed, the
/// channel photo in the look's temporary folder (null when not asked for or not fetched) and that folder (deleted by
/// <see cref="IChannelInspirer.Discard"/>).
/// </summary>
public sealed record ChannelInspiration(ChannelListing Listing, ChannelFacts Facts, ReferenceTempoProfile? Tempo, int Measured, int Failed, string? PhotoFile,
    string Folder);
