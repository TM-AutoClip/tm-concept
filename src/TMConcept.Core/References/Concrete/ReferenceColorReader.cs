namespace TMConcept.Core.References;

/// <summary>
/// The colour of a reference from small RGB frames (<see cref="ReferenceLimits.ColorWidth"/> × <see cref="ReferenceLimits.ColorHeight"/>, three
/// bytes a pixel, as ffmpeg writes them with <c>format=rgb24 -f rawvideo</c>), one frame at a time: the mean CIELAB (ReelMimic <c>look.py</c>
/// measures the colour arc in CIELAB, MIT), the mean brightness (luma) and saturation (HSV), and a sample of pixels whose clusters
/// (<see cref="PaletteClusters"/>) are the main colours. The sample is thinned evenly when it is full, so a long video needs no more memory
/// than a short one. Pure arithmetic.
/// </summary>
public sealed class ReferenceColorReader
{
    private const int Pixels = ReferenceLimits.ColorWidth * ReferenceLimits.ColorHeight;

    private readonly List<(double L, double A, double B)> _sample = [];
    private double _l;
    private double _a;
    private double _b;
    private double _brightness;
    private double _saturation;
    private long _count;
    private long _seen;
    private int _stride = 1;

    /// <summary>Bytes of one frame.</summary>
    public const int FrameBytes = Pixels * 3;

    /// <summary>Frames read.</summary>
    public int Frames { get; private set; }

    /// <summary>Adds the next frame (exactly <see cref="FrameBytes"/> bytes).</summary>
    public void Add(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != FrameBytes)
        {
            throw new ArgumentException("A colour frame of the reference measure has " + FrameBytes + " bytes.", nameof(frame));
        }

        for (var i = 0; i < FrameBytes; i += 3)
        {
            var (r, g, b) = (frame[i], frame[i + 1], frame[i + 2]);
            var lab = LabColor.FromRgb(r, g, b);
            _l += lab.L;
            _a += lab.A;
            _b += lab.B;
            _brightness += ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            _saturation += max == 0 ? 0 : (max - min) / (double)max;
            _count++;
            if (_seen++ % _stride == 0)
            {
                _sample.Add(lab);
                if (_sample.Count >= ReferenceLimits.PaletteSamples)
                {
                    // Full: keep every second sample and take every second pixel from now on.
                    for (var k = 0; k < _sample.Count / 2; k++)
                    {
                        _sample[k] = _sample[2 * k];
                    }

                    _sample.RemoveRange(_sample.Count / 2, _sample.Count - (_sample.Count / 2));
                    _stride *= 2;
                }
            }
        }

        Frames++;
    }

    /// <summary>Every frame of <paramref name="stream"/> (a last part shorter than a frame is left out).</summary>
    public async Task AddAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffer = new byte[FrameBytes];
        while (true)
        {
            var read = await stream.ReadAtLeastAsync(buffer, FrameBytes, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            if (read < FrameBytes)
            {
                return;
            }

            Add(buffer);
        }
    }

    /// <summary>The colour read so far; null before the first frame.</summary>
    public ReferenceColors? Result()
    {
        if (_count == 0)
        {
            return null;
        }

        return new ReferenceColors(_l / _count, _a / _count, _b / _count, _brightness / _count, _saturation / _count,
            PaletteClusters.Main(_sample, ReferenceLimits.PaletteSize));
    }
}
