namespace TMConcept.Core.Images;

/// <summary>The video failure codes of an <see cref="ImageEngineProblem"/> (each has its <c>video.error.&lt;code&gt;</c> text).</summary>
public static class ImageEngineProblems
{
    /// <summary>The image engine cannot run on this computer (a missing driver library for the chosen build).</summary>
    public const string Images = "images";

    /// <summary>
    /// The Microsoft Visual C++ 2015–2022 runtime is missing (Windows): every stable-diffusion.cpp build needs it and none ships it; the
    /// screen names the redistributable to install.
    /// </summary>
    public const string VisualCppRuntime = "image_runtime";
}
