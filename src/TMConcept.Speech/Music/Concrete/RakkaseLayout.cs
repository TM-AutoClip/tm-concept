using TMConcept.Core.Paths;

namespace TMConcept.Speech.Music;

/// <summary>
/// Where the local music model lives (0.1.46, C5): everything under <c>&lt;tools&gt;/music-py</c>. <c>rakkase/</c> its source with its
/// <c>ayarlar.json</c>, <c>ace-step/</c> ACE-Step 1.5's source with its <c>.venv</c> (made by uv) and <c>checkpoints/</c> (the weights, a
/// component of their own), <c>downloads/</c> the source archives while they are unpacked, <c>home/</c> the folder its libraries write to.
/// </summary>
public sealed class RakkaseLayout(IAppPaths paths)
{
    /// <summary>The marker of a finished install (holds what was installed).</summary>
    public const string Marker = ".tmc-installed";

    /// <summary>The root.</summary>
    public string Root => Path.Combine(paths.ToolsDirectory, "music-py");

    /// <summary>rakkase's folder.</summary>
    public string RakkaseFolder => Path.Combine(Root, "rakkase");

    /// <summary>rakkase's entry script.</summary>
    public string Script => Path.Combine(RakkaseFolder, "rakkase.py");

    /// <summary>rakkase's settings (written by the install, as its own <c>kur.ps1</c> does).</summary>
    public string Settings => Path.Combine(RakkaseFolder, "ayarlar.json");

    /// <summary>rakkase's list of the weights (pinned with its source).</summary>
    public string WeightList => Path.Combine(RakkaseFolder, "scripts", "modeller.json");

    /// <summary>Where rakkase keeps every finished piece (emptied after each piece the app made).</summary>
    public string Productions => Path.Combine(RakkaseFolder, "uretimler");

    /// <summary>ACE-Step's folder (rakkase's <c>ace_kok</c>).</summary>
    public string AceFolder => Path.Combine(Root, "ace-step");

    /// <summary>ACE-Step's environment (made by <c>uv sync</c>).</summary>
    public string Environment => Path.Combine(AceFolder, ".venv");

    /// <summary>The environment's interpreter.</summary>
    public string Python => OperatingSystem.IsWindows()
        ? Path.Combine(Environment, "Scripts", "python.exe")
        : Path.Combine(Environment, "bin", "python");

    /// <summary>The weights (where ACE-Step and rakkase read them).</summary>
    public string Checkpoints => Path.Combine(AceFolder, "checkpoints");

    /// <summary>The environment's marker.</summary>
    public string EnvironmentMarker => Path.Combine(Environment, Marker);

    /// <summary>The weights' marker.</summary>
    public string WeightsMarker => Path.Combine(Checkpoints, Marker);

    /// <summary>The source archives while they are unpacked.</summary>
    public string Downloads => Path.Combine(Root, "downloads");

    /// <summary>The folder its libraries write their settings and caches to (instead of the user's home).</summary>
    public string Home => Path.Combine(Root, "home");
}
