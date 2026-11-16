namespace TMConcept.Engines.Images;

/// <summary>What a weights file of a local image model is, and so which sd-cli option names it.</summary>
public enum ImageFileRole
{
    /// <summary>The diffusion model alone (<c>--diffusion-model</c>): FLUX, Z-Image, Qwen-Image.</summary>
    Diffusion = 1,

    /// <summary>A whole checkpoint with its own text encoders and VAE inside (<c>-m</c>): Stable Diffusion 3.5, SDXL.</summary>
    Full = 2,

    /// <summary>A language model used as the text encoder (<c>--llm</c>): Qwen3, Qwen3-VL, Qwen2.5-VL.</summary>
    Llm = 3,

    /// <summary>CLIP-L text encoder (<c>--clip_l</c>).</summary>
    ClipL = 4,

    /// <summary>CLIP-G text encoder (<c>--clip_g</c>).</summary>
    ClipG = 5,

    /// <summary>T5-XXL text encoder (<c>--t5xxl</c>).</summary>
    T5xxl = 6,

    /// <summary>The VAE that turns the result into pixels (<c>--vae</c>).</summary>
    Vae = 7,
}
