namespace TMConcept.Core.Speech;

/// <summary>Codes of the local voice engines (the narration step's local models name their engine by this code as the model id).</summary>
public static class SpeechEngines
{
    /// <summary>Local Supertonic 3 (the suggested narration).</summary>
    public const string Supertonic = "supertonic";

    /// <summary>Local Kokoro 82M (English only).</summary>
    public const string Kokoro = "kokoro";

    /// <summary>Local Pocket TTS (English only; speaks with the user's own voice sample).</summary>
    public const string Pocket = "pocket";

    /// <summary>Local Chatterbox Multilingual V3 (Turkish and nine more languages; speaks with the user's own voice sample).</summary>
    public const string Chatterbox = "chatterbox";

    /// <summary>Local XTTS-v2 (Coqui, Python; non-commercial licence; 17 languages; built-in speakers or the user's own voice sample).</summary>
    public const string XttsV2 = "xtts-v2";

    /// <summary>Local F5-TTS with a Turkish fine-tune (Python; CC BY-NC 4.0; Turkish only).</summary>
    public const string F5Turkish = "f5-tts-tr";

    /// <summary>Local Trendyol-TTS (VoxCPM2 with a merged Turkish LoRA, Python; MIT; Turkish only; a graphics card of 8 GB).</summary>
    public const string Trendyol = "trendyol-tts";

    /// <summary>Local Ahenk (VoxCPM2 with a Turkish LoRA, Python; CC BY 4.0, attribution; Turkish only; voices are emotions).</summary>
    public const string Ahenk = "ahenk";

    /// <summary>Local VoxCPM2 (OpenBMB, Python; Apache-2.0; many languages; copies the user's own voice sample).</summary>
    public const string VoxCpm2 = "voxcpm2";

    /// <summary>Local MMS-TTS Turkish (Meta, Python; CC BY-NC 4.0; small, on the processor).</summary>
    public const string MmsTurkish = "mms-tts-tr";

    /// <summary>Local Ahenk Cep (Turkish only; seven speaking styles, 48 kHz).</summary>
    public const string AhenkCep = "ahenk-cep";
}
