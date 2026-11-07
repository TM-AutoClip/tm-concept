using System.Text.Json;
using TMConcept.Core.Render;

namespace TMConcept.Core.Concepts;

/// <summary>The focus drop-downs embedded in Core (<c>TMConcept.Focus.json</c>), read once.</summary>
public sealed class EmbeddedFocusSource : IFocusSource
{
    private const string Resource = "TMConcept.Focus.json";

    /// <summary>Reads the embedded file.</summary>
    public EmbeddedFocusSource()
    {
        using var stream = typeof(EmbeddedFocusSource).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("The focus file is missing from the build.");
        var file = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.FocusCatalogFile) ?? throw new InvalidDataException("Empty focus file.");
        Categories = file.Categories;
    }

    /// <inheritdoc />
    public IReadOnlyList<FocusCategory> Categories { get; }

    /// <inheritdoc />
    public FocusCategory? ForTemplate(string? templateId)
    {
        var id = (templateId ?? string.Empty).Split('.')[0];
        return id.Length == 0 || id == FocusCatalogFile.GeneralId ? null : Categories.FirstOrDefault(c => c.Id == id);
    }
}
