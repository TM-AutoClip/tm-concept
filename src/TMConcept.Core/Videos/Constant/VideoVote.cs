namespace TMConcept.Core.Videos;

/// <summary>
/// The user's rating of a made video (the number is stored; new values are added at the end). No rating = neutral: producing a video does
/// not count as liking it.
/// </summary>
public enum VideoVote
{
    /// <summary>"I liked it": its topic becomes a positive example (similar spirit, different topics).</summary>
    Like = 1,

    /// <summary>"I did not like it": its topic becomes a strong negative (stay away from it and close topics).</summary>
    Dislike = 2,
}
