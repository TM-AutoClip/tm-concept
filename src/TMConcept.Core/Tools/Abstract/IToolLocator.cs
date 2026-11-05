namespace TMConcept.Core.Tools;

/// <summary>Where a downloaded tool is and whether it is ready (the tool installer; parts that only need to find a tool take this).</summary>
public interface IToolLocator
{
    /// <summary>Whether <paramref name="tool"/> is ready and where its main file is.</summary>
    ToolStatus Status(ToolKind tool);
}
