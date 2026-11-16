using TMConcept.Core.Hardware;

namespace TMConcept.Desktop.Paths;

/// <summary>
/// The Image acceleration setting kept in preferences.json (<see cref="UiPreferences.ImageAcceleration"/>): read from the file once, then
/// from memory (the image build is chosen whenever a component's readiness is asked); <see cref="Change"/> writes it through.
/// </summary>
public sealed class PreferencesImageAcceleration(UiPreferencesFile preferences) : IImageAccelerationSetting
{
    private ImageAcceleration? _value;

    /// <inheritdoc />
    public ImageAcceleration Value => _value ??= Valid(preferences.Read().ImageAcceleration);

    /// <summary>Stores a new setting (used from the next image).</summary>
    public void Change(ImageAcceleration value)
    {
        _value = Valid(value);
        preferences.Change(p => p.ImageAcceleration = _value.Value);
    }

    // A number the app does not know (a hand-edited file) means automatic.
    private static ImageAcceleration Valid(ImageAcceleration value) => Enum.IsDefined(value) ? value : ImageAcceleration.Automatic;
}
