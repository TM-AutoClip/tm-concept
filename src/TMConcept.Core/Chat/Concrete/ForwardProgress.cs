namespace TMConcept.Core.Chat;

/// <summary>
/// An <see cref="IProgress{T}"/> that hands every report on at once, on the thread that reports (unlike <see cref="Progress{T}"/>, which
/// posts): a service turns one progress into another with it, and the page's own <see cref="Progress{T}"/> at the end brings it to the screen.
/// </summary>
public sealed class ForwardProgress<T>(Action<T> report) : IProgress<T>
{
    /// <inheritdoc />
    public void Report(T value) => report(value);
}
