using System.Text.Json;
using TMConcept.Core.Render;

namespace TMConcept.Core.Templates;

/// <summary>Templates embedded in Core (<c>TMConcept.Templates.*.json</c>). Read once; each call gets its own copy, so nobody can change them.</summary>
public sealed class EmbeddedTemplateSource : ITemplateSource
{
    private const string Prefix = "TMConcept.Templates.";
    private readonly IReadOnlyList<string> _json;

    /// <summary>Reads the embedded files.</summary>
    public EmbeddedTemplateSource()
    {
        var assembly = typeof(EmbeddedTemplateSource).Assembly;
        _json = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            })
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ChannelTemplate> All => _json
        .Select(j => JsonSerializer.Deserialize(j, CoreJsonContext.Default.ChannelTemplate) ?? throw new InvalidDataException("Empty template file."))
        .OrderBy(t => t.Id, StringComparer.Ordinal)
        .ToList();

    /// <inheritdoc />
    public ChannelTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
