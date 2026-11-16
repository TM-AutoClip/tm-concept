namespace TMConcept.Core.Hardware;

/// <summary>The user's "Image acceleration" setting of this computer (stored per machine, outside the database).</summary>
public interface IImageAccelerationSetting
{
    /// <summary>The current setting (<see cref="ImageAcceleration.Automatic"/> until chosen).</summary>
    ImageAcceleration Value { get; }
}
