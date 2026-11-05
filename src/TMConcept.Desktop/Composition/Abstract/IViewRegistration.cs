namespace TMConcept.Desktop.Composition;

/// <summary>A view model → view mapping contributed by a page or a settings section (collected from DI into the view locator).</summary>
public interface IViewRegistration
{
    /// <summary>Adds the mapping.</summary>
    void Register(ViewLocator locator);
}
