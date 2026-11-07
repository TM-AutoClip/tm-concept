namespace TMConcept.Storage.Database;

/// <summary>The one value of <c>pragma_user_version</c> (read only): the version 0.1.6 wrote, 0 in every other file.</summary>
internal sealed class UserVersionRow
{
    /// <summary>Value.</summary>
    public long Value { get; set; }
}
