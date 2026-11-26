using TMConcept.Core.Tools;

namespace TMConcept.Engines.Music;

/// <summary>
/// The pinned SoundFont of the composed music (0.1.55, "Program composes"): MuseScore General 0.2 (13 May 2020; MIT, FluidR3 by Frank Wen,
/// FluidR3Mono by Michael Cowgill, adapted by S. Christian Collins; its licence file MuseScore_General_License.md, read on 26 Nov 2026), the
/// compressed .sf3 file of the official MuseScore mirror at ftp.osuosl.org (served directly, no redirect, ranges allowed). Downloaded and
/// measured on 26 Nov 2026: 39 900 972 bytes, SHA-256 below. Nothing is downloaded without the user's yes (rule 18).
/// </summary>
public static class SoundFontPins
{
    /// <summary>The SoundFont's version.</summary>
    public const string Version = "0.2";

    /// <summary>Its host.</summary>
    public const string Host = "ftp.osuosl.org";

    /// <summary>The file's name.</summary>
    public const string FileName = "MuseScore_General.sf3";

    /// <summary>Its size.</summary>
    public const long Bytes = 39_900_972;

    /// <summary>The name of the licence note written next to it (every downloaded model part keeps its licence beside it).</summary>
    public const string LicenseFileName = "LICENSE.txt";

    /// <summary>The licence note: the notices its licence file asks to keep, and where the full text is.</summary>
    public const string LicenseNotice = """
        MuseScore General SoundFont 0.2 (MuseScore_General.sf3) - MIT License.
        Full licence: https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/MuseScore_General_License.md

        FluidR3 (original version) by Frank Wen Copyright (c) 2000-02
        Mono conversion (FluidR3Mono) by Michael Cowgill Copyright (c) 2014-17
        Adaptation for MuseScore_General.sf2 by S. Christian Collins Copyright (c) 2018-19
        Temple Blocks instrument provided by Ethan Winer Copyright (c) 2002
        Drumline Cymbals provided by Michael Schorsch Copyright (c) 2016

        Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
        "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
        distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
        the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions
        of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
        WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
        BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
        CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
        """;

    /// <summary>Its download.</summary>
    public static readonly ToolArchive Archive = new(ToolKind.SoundFont, Version, "any",
        new Uri("https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/" + FileName),
        "5b85b6c2c61d10b2b91cddd41efcce7b25cd31c8271d511c73afafbef20b6fa3", string.Empty, FileName, Bytes);
}
