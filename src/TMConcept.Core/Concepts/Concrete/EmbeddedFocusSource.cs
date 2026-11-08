using System.Text.Json;
using TMConcept.Core.Render;

namespace TMConcept.Core.Concepts;

/// <summary>The fine-tuning drop-downs embedded in Core (<c>TMConcept.Focus.json</c>), read once.</summary>
public sealed class EmbeddedFocusSource : IFocusSource
{
    private const string Resource = "TMConcept.Focus.json";

    /// <summary>Reads the embedded file.</summary>
    public EmbeddedFocusSource()
    {
        using var stream = typeof(EmbeddedFocusSource).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("The focus file is missing from the build.");
        var file = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.FocusCatalogFile) ?? throw new InvalidDataException("Empty focus file.");
        Dimensions = file.Dimensions;
    }

    /// <inheritdoc />
    public IReadOnlyList<FocusDimension> Dimensions { get; }
}
