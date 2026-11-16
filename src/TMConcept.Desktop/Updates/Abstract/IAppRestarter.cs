namespace TMConcept.Desktop.Updates;

/// <summary>Starts the program again after an update and ends this one (tests record the call instead).</summary>
public interface IAppRestarter
{
    /// <summary>Starts <paramref name="program"/> (it waits for this process to end) and closes this app.</summary>
    void Restart(string program);
}
