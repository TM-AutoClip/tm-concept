using TMConcept.Core.Styles;

namespace TMConcept.Tests.Desktop;

/// <summary>A template store in memory whose writes fail with <see cref="Error"/> (a locked or busy database).</summary>
internal sealed class FailingStyleStore : IVideoStyleStore
{
    public List<VideoStyle> Styles { get; } = [];

    public Exception Error { get; set; } = new InvalidOperationException("the database is being restored");

    public Task<IReadOnlyList<VideoStyle>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VideoStyle>>(Styles.Select(VideoStyleRules.Copy).ToList());

    public Task<VideoStyle?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Styles.FirstOrDefault(s => s.Id == id) is { } style ? VideoStyleRules.Copy(style) : null);

    public Task SaveAsync(VideoStyle style, CancellationToken cancellationToken) => Task.FromException(Error);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.FromException(Error);
}
