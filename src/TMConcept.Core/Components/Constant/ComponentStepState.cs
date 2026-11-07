namespace TMConcept.Core.Components;

/// <summary>Where one component of an install stands (the install window shows one line per component; new values are added at the end).</summary>
public enum ComponentStepState
{
    /// <summary>Not started yet.</summary>
    Waiting = 0,

    /// <summary>Downloading or unpacking now.</summary>
    Running = 1,

    /// <summary>Installed.</summary>
    Done = 2,

    /// <summary>Could not be installed (<see cref="ComponentStep.Detail"/> says why).</summary>
    Failed = 3,

    /// <summary>Not tried: something it depends on could not be installed.</summary>
    Skipped = 4,
}
