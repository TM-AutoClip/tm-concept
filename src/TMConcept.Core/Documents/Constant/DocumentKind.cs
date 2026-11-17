namespace TMConcept.Core.Documents;

/// <summary>What a document of "Belgelerim" was made from (stored as its number; new kinds go at the end).</summary>
public enum DocumentKind
{
    /// <summary>A PDF file (text layer only; no OCR).</summary>
    Pdf = 0,

    /// <summary>A Word document (.docx).</summary>
    Docx = 1,

    /// <summary>A plain text file (.txt).</summary>
    Text = 2,

    /// <summary>A Markdown file (.md).</summary>
    Markdown = 3,

    /// <summary>A web page (its readable text, fetched once; no script ever runs).</summary>
    Web = 4,

    /// <summary>A Wikipedia article in any language (its plain text from Wikipedia's API).</summary>
    Wikipedia = 5,
}
