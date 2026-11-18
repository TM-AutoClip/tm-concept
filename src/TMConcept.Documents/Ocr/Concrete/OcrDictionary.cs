namespace TMConcept.Documents.Ocr;

/// <summary>
/// The recogniser's character list (CTC): class 0 is the blank, class i (1 … n) is the i-th character of the list, and the class after the
/// list is a space (PaddleOCR's <c>use_space_char</c>). The Latin list is bundled (<see cref="LocalOcrModels.DictionaryResource"/>).
/// </summary>
public sealed class OcrDictionary
{
    private readonly string[] _characters;

    /// <summary>A list of <paramref name="characters"/> (one text element each).</summary>
    public OcrDictionary(IEnumerable<string> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        _characters = characters.ToArray();
        SmallI = Class("i");
        DotlessI = Class("ı");
        CapitalI = Class("I");
        DottedCapitalI = Class("İ");
    }

    /// <summary>The class of i (0 = not in the list).</summary>
    public int SmallI { get; }

    /// <summary>The class of the Turkish dotless ı (0 = not in the list).</summary>
    public int DotlessI { get; }

    /// <summary>The class of I (0 = not in the list).</summary>
    public int CapitalI { get; }

    /// <summary>The class of the Turkish dotted capital İ (0 = not in the list).</summary>
    public int DottedCapitalI { get; }

    /// <summary>The characters, in class order (without the blank and the space).</summary>
    public IReadOnlyList<string> Characters => _characters;

    /// <summary>The classes a recogniser of this list gives: the blank, the characters and the space.</summary>
    public int Classes => _characters.Length + 2;

    /// <summary>The bundled Latin list of the PP-OCRv5 recogniser.</summary>
    public static OcrDictionary Latin()
    {
        using var stream = typeof(OcrDictionary).Assembly.GetManifestResourceStream(LocalOcrModels.DictionaryResource)
            ?? throw new InvalidOperationException("The OCR character list is missing from the app.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new OcrDictionary(lines);
    }

    /// <summary>The class of <paramref name="character"/> (0 = not in the list).</summary>
    public int Class(string character) => Array.IndexOf(_characters, character) + 1;

    /// <summary>The text of class <paramref name="index"/> (empty for the blank or a class outside the list).</summary>
    public string Text(int index) => index switch
    {
        <= 0 => string.Empty,
        _ when index <= _characters.Length => _characters[index - 1],
        _ when index == _characters.Length + 1 => " ",
        _ => string.Empty,
    };
}
