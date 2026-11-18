namespace TMConcept.Core.Hardware;

/// <summary>The user's "Text recognition" setting of this computer (0.1.27; stored per machine, outside the database).</summary>
public interface IOcrAccelerationSetting
{
    /// <summary>The current setting (<see cref="OcrAcceleration.Automatic"/> until chosen).</summary>
    OcrAcceleration Value { get; }
}
