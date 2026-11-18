using TMConcept.Core.Hardware;

namespace TMConcept.Desktop.Paths;

/// <summary>
/// The "Text recognition" setting kept in preferences.json (<see cref="UiPreferences.OcrAcceleration"/>, 0.1.27): read from the file once,
/// then from memory (the local OCR asks it at every page); <see cref="Change"/> writes it through.
/// </summary>
public sealed class PreferencesOcrAcceleration(UiPreferencesFile preferences) : IOcrAccelerationSetting
{
    private OcrAcceleration? _value;

    /// <inheritdoc />
    public OcrAcceleration Value => _value ??= Valid(preferences.Read().OcrAcceleration);

    /// <summary>Stores a new setting (used from the next page).</summary>
    public void Change(OcrAcceleration value)
    {
        _value = Valid(value);
        preferences.Change(p => p.OcrAcceleration = _value.Value);
    }

    // A number the app does not know (a hand-edited file) means automatic.
    private static OcrAcceleration Valid(OcrAcceleration value) => Enum.IsDefined(value) ? value : OcrAcceleration.Automatic;
}
