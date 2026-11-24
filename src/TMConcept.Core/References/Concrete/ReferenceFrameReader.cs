using TMConcept.Core.Videos;

namespace TMConcept.Core.References;

/// <summary>
/// Turns the raw grey frames of a reference (<see cref="MotionCheckLimits.FrameWidth"/> × <see cref="MotionCheckLimits.FrameHeight"/>, one byte a
/// pixel, as ffmpeg writes them with <c>format=gray -f rawvideo</c>) into <see cref="ReferenceFrameSeries"/>, one frame at a time (only the frame
/// before is kept): the change and the cut-grid share of the motion check (K07, <see cref="MotionFrameReader"/>'s numbers), the correlation of
/// the two grey histograms (a cut changes the histogram, a fast pan only the pixels; ReelMimic <c>looks_like_cut</c>, MIT) and the camera's
/// shift. The shift is found on the frames' row and column means (the content's shift along each axis, with a parabola through the best
/// whole-pixel shift for the fraction), then checked on the pixels: moving the frame before by it must explain the frame clearly better than
/// standing still (ReelMimic <c>camera.py</c> photometric check, MIT). No OpenCV: tracking corners is not needed for "how much and which way".
/// Pure arithmetic.
/// </summary>
public sealed class ReferenceFrameReader(double fps)
{
    private const int Width = MotionCheckLimits.FrameWidth;
    private const int Height = MotionCheckLimits.FrameHeight;
    private const int Grid = MotionCheckLimits.Grid;
    private const int Bins = ReferenceLimits.HistogramBins;
    private const int Max = ReferenceLimits.MaxShift;

    private readonly List<double> _change = [];
    private readonly List<double> _cutShare = [];
    private readonly List<double> _correlation = [];
    private readonly List<double> _shiftX = [];
    private readonly List<double> _shiftY = [];
    private readonly List<bool> _moved = [];
    private readonly double[] _cells = new double[Grid * Grid];
    private readonly double[] _histogram = new double[Bins];
    private readonly double[] _previousHistogram = new double[Bins];
    private readonly double[] _columns = new double[Width];
    private readonly double[] _rows = new double[Height];
    private readonly double[] _previousColumns = new double[Width];
    private readonly double[] _previousRows = new double[Height];
    private byte[]? _previous;

    /// <summary>Bytes of one frame.</summary>
    public const int FrameBytes = Width * Height;

    /// <summary>Adds the next frame (exactly <see cref="FrameBytes"/> bytes).</summary>
    public void Add(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != FrameBytes)
        {
            throw new ArgumentException("A grey frame of the reference measure has " + FrameBytes + " bytes.", nameof(frame));
        }

        Array.Clear(_histogram);
        Array.Clear(_columns);
        Array.Clear(_rows);
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;
            for (var x = 0; x < Width; x++)
            {
                var pixel = frame[row + x];
                _histogram[pixel * Bins / 256]++;
                _columns[x] += pixel;
                _rows[y] += pixel;
            }
        }

        for (var x = 0; x < Width; x++)
        {
            _columns[x] /= Height;
        }

        for (var y = 0; y < Height; y++)
        {
            _rows[y] /= Width;
        }

        if (_previous is null)
        {
            _previous = frame.ToArray();
            _change.Add(0);
            _cutShare.Add(0);
            _correlation.Add(1);
            _shiftX.Add(0);
            _shiftY.Add(0);
            _moved.Add(false);
            Keep();
            return;
        }

        var (change, share) = Difference(frame, _previous);
        _change.Add(change);
        _cutShare.Add(share);
        _correlation.Add(Correlation(_previousHistogram, _histogram));

        // The content's shift along each axis: cur[x] ≈ prev[x − d]. The camera moved the other way.
        var dx = Shift(_previousColumns, _columns);
        var dy = Shift(_previousRows, _rows);
        var moved = false;
        var whole = (X: (int)Math.Round(dx), Y: (int)Math.Round(dy));
        if (change >= ReferenceLimits.StillChange && (whole.X != 0 || whole.Y != 0))
        {
            var still = Error(frame, _previous, 0, 0);
            var shifted = Error(frame, _previous, whole.X, whole.Y);
            moved = shifted <= ReferenceLimits.CameraGain * still;
        }

        var speed = Math.Sqrt((dx * dx) + (dy * dy)) / Width * fps;
        moved &= speed >= ReferenceLimits.CameraSpeed;
        _shiftX.Add(moved ? -dx : 0);
        _shiftY.Add(moved ? -dy : 0);
        _moved.Add(moved);
        frame.CopyTo(_previous);
        Keep();
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

    /// <summary>What was read so far.</summary>
    public ReferenceFrameSeries Series() => new(fps, [.. _change], [.. _cutShare], [.. _correlation], [.. _shiftX], [.. _shiftY], [.. _moved]);

    /// <summary>
    /// The shift <c>d</c> (−<see cref="ReferenceLimits.MaxShift"/> … +<see cref="ReferenceLimits.MaxShift"/>, with a fraction) for which
    /// <c>current[i] ≈ previous[i − d]</c>, by the smallest mean absolute difference over the overlap. Public for the tests.
    /// </summary>
    public static double Shift(IReadOnlyList<double> previous, IReadOnlyList<double> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var length = Math.Min(previous.Count, current.Count);
        var errors = new double[(2 * Max) + 1];
        for (var d = -Max; d <= Max; d++)
        {
            var sum = 0.0;
            var count = 0;
            for (var i = Max; i < length - Max; i++)
            {
                sum += Math.Abs(current[i] - previous[i - d]);
                count++;
            }

            errors[d + Max] = count > 0 ? sum / count : double.MaxValue;
        }

        var best = 0;
        for (var k = 1; k < errors.Length; k++)
        {
            // A tie keeps the smaller shift (standing still wins over a guess).
            if (errors[k] < errors[best] - 1e-12 || (Math.Abs(errors[k] - errors[best]) <= 1e-12 && Math.Abs(k - Max) < Math.Abs(best - Max)))
            {
                best = k;
            }
        }

        var fraction = 0.0;
        if (best > 0 && best < errors.Length - 1)
        {
            var (a, b, c) = (errors[best - 1], errors[best], errors[best + 1]);
            var bend = a - (2 * b) + c;
            if (bend > 1e-12)
            {
                fraction = Math.Clamp((a - c) / (2 * bend), -0.5, 0.5);
            }
        }

        return best - Max + fraction;
    }

    /// <summary>The Pearson correlation of two histograms (OpenCV's HISTCMP_CORREL); 1 when both are flat.</summary>
    public static double Correlation(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var n = Math.Min(first.Count, second.Count);
        double meanA = 0, meanB = 0;
        for (var i = 0; i < n; i++)
        {
            meanA += first[i];
            meanB += second[i];
        }

        meanA /= n;
        meanB /= n;
        double cross = 0, varA = 0, varB = 0;
        for (var i = 0; i < n; i++)
        {
            var a = first[i] - meanA;
            var b = second[i] - meanB;
            cross += a * b;
            varA += a * a;
            varB += b * b;
        }

        var scale = Math.Sqrt(varA * varB);
        return scale < 1e-12 ? 1 : cross / scale;
    }

    private (double Change, double Share) Difference(ReadOnlySpan<byte> frame, byte[] previous)
    {
        Array.Clear(_cells);
        long whole = 0;
        const int cellWidth = Width / Grid;
        const int cellHeight = Height / Grid;
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;
            var cellRow = Math.Min(y / cellHeight, Grid - 1) * Grid;
            for (var x = 0; x < Width; x++)
            {
                var difference = Math.Abs(frame[row + x] - previous[row + x]);
                whole += difference;
                _cells[cellRow + Math.Min(x / cellWidth, Grid - 1)] += difference;
            }
        }

        var big = 0;
        const double cellPixels = 255.0 * cellWidth * cellHeight;
        foreach (var cell in _cells)
        {
            if (cell / cellPixels > MotionCheckLimits.CutCellChange)
            {
                big++;
            }
        }

        return (whole / (255.0 * FrameBytes), big / (double)(Grid * Grid));
    }

    /// <summary>The mean absolute difference (0–1) of the inner frame between the frame and the one before moved by (dx, dy).</summary>
    private static double Error(ReadOnlySpan<byte> frame, byte[] previous, int dx, int dy)
    {
        long sum = 0;
        var count = 0;
        for (var y = Max; y < Height - Max; y++)
        {
            var row = y * Width;
            var from = (y - dy) * Width;
            for (var x = Max; x < Width - Max; x++)
            {
                sum += Math.Abs(frame[row + x] - previous[from + x - dx]);
                count++;
            }
        }

        return sum / (255.0 * count);
    }

    private void Keep()
    {
        Array.Copy(_histogram, _previousHistogram, Bins);
        Array.Copy(_columns, _previousColumns, Width);
        Array.Copy(_rows, _previousRows, Height);
    }
}
