namespace TMConcept.Core.Plans;

/// <summary>What a scene of the app's drawer stands on.</summary>
public static class PlanBases
{
    /// <summary>A generated image (with overlays on spots of it).</summary>
    public const string Image = "image";

    /// <summary>A map drawn by the app from Natural Earth (overlays on places by longitude and latitude).</summary>
    public const string Map = "map";

    /// <summary>A diagram of labelled boxes and links drawn by the app.</summary>
    public const string Diagram = "diagram";

    /// <summary>A title or statement card.</summary>
    public const string Title = "title";

    /// <summary>Every base.</summary>
    public static readonly IReadOnlyList<string> All = [Image, Map, Diagram, Title];
}
