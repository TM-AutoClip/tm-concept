using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TMConcept.Core.Render;

/// <summary>Builds and serializes the render input.</summary>
public static class RenderInputBuilder
{
    /// <summary>The input's file name in an engine project (the engines' skeletons read it under this name).</summary>
    public const string FileName = "input.json";

    /// <summary>Indented JSON for the work folder (Turkish letters unescaped).</summary>
    public static JsonSerializerOptions FileJson { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = CoreJsonContext.Default,
    };

    private static readonly JsonSerializerOptions HtmlJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.Default,
        TypeInfoResolver = CoreJsonContext.Default,
    };

    /// <summary>
    /// Builds the input from scene lengths: frames = ceil(ms × fps / 1000), scenes one after another, at least
    /// <see cref="RenderTiming.SceneMinMs"/> each, <see cref="RenderTiming.ClosingMs"/> added to the last scene.
    /// </summary>
    public static RenderInput Build(string title, string language, IReadOnlyList<RenderSceneSource> scenes, int width, int height, int fps,
        RenderTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        if (scenes.Count == 0)
        {
            throw new ArgumentException("At least one scene is needed.", nameof(scenes));
        }

        if (fps <= 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fps), "Size and fps must be positive.");
        }

        var input = new RenderInput { Width = width, Height = height, Fps = fps, Language = language, Title = title, Theme = theme ?? new RenderTheme() };
        var frame = 0;
        for (var i = 0; i < scenes.Count; i++)
        {
            var source = scenes[i];
            var ms = Math.Max(RenderTiming.SceneMinMs, source.AudioMs) + (i == scenes.Count - 1 ? RenderTiming.ClosingMs : 0);
            var count = (int)Math.Ceiling(ms * fps / 1000.0);
            input.Scenes.Add(new RenderScene
            {
                Number = i + 1,
                StartFrame = frame,
                FrameCount = count,
                StartSeconds = Math.Round(frame / (double)fps, 3),
                DurationSeconds = Math.Round(count / (double)fps, 3),
                Visual = source.Visual,
                Caption = source.Caption,
                Image = source.Image,
            });
            frame += count;
        }

        input.TotalFrames = frame;
        input.DurationSeconds = Math.Round(frame / (double)fps, 3);
        return input;
    }

    /// <summary>Indented JSON for <c>input.json</c>.</summary>
    public static string ToJson(RenderInput input) => JsonSerializer.Serialize(input, Info(FileJson));

    /// <summary>Reads <c>input.json</c>; <c>null</c> when it is not valid.</summary>
    public static RenderInput? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, Info(FileJson));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// JSON for a <c>&lt;script&gt;</c> block of an HTML page: <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, quotes and non-ASCII escaped, so a
    /// <c>&lt;/script&gt;</c> inside a text can never leave the script block (text stays data).
    /// </summary>
    public static string ToHtmlSafeJson(RenderInput input) => JsonSerializer.Serialize(input, Info(HtmlJson));

    /// <summary>The source-generated contract of <see cref="RenderInput"/> under <paramref name="options"/> (no reflection).</summary>
    private static JsonTypeInfo<RenderInput> Info(JsonSerializerOptions options) => (JsonTypeInfo<RenderInput>)options.GetTypeInfo(typeof(RenderInput));

    /// <summary>Video size of a format, scaled (even numbers; H.264 rejects odd sizes).</summary>
    public static (int Width, int Height) Size(Profiles.VideoFormat format, double scale = 1)
    {
        var (w, h) = format == Profiles.VideoFormat.Vertical ? (1080, 1920) : (1920, 1080);
        static int Even(double x) => Math.Max(2, (int)Math.Round(x / 2) * 2);
        return (Even(w * scale), Even(h * scale));
    }
}
