namespace TMConcept.Core.Videos;

/// <summary>
/// Turns raw grey frames (<see cref="MotionCheckLimits.FrameWidth"/> × <see cref="MotionCheckLimits.FrameHeight"/>, one byte a pixel, as
/// ffmpeg writes them with <c>format=gray -f rawvideo</c>) into the numbers of the motion check (<see cref="MotionSeries"/>), one frame at a
/// time: only the frame before is kept, so a long video needs no more memory than a short one. Pure arithmetic; no ffmpeg here.
/// </summary>
public sealed class MotionFrameReader(double fps)
{
    private const int Width = MotionCheckLimits.FrameWidth;
    private const int Height = MotionCheckLimits.FrameHeight;
    private const int Grid = MotionCheckLimits.Grid;

    private readonly List<double> _luminance = [];
    private readonly List<double> _change = [];
    private readonly List<double> _cutShare = [];
    private readonly double[] _cells = new double[Grid * Grid];
    private byte[]? _previous;

    /// <summary>Bytes of one frame.</summary>
    public const int FrameBytes = Width * Height;

    /// <summary>Adds the next frame (exactly <see cref="FrameBytes"/> bytes).</summary>
    public void Add(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != FrameBytes)
        {
            throw new ArgumentException("A grey frame of the motion check has " + FrameBytes + " bytes.", nameof(frame));
        }

        long sum = 0;
        foreach (var pixel in frame)
        {
            sum += pixel;
        }

        _luminance.Add(sum / (255.0 * FrameBytes));
        if (_previous is null)
        {
            _previous = frame.ToArray();
            _change.Add(0);
            _cutShare.Add(0);
            return;
        }

        // The change of every pixel, summed per cell of the 8 × 8 grid (a cell is 20 × 12 pixels).
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
                var difference = Math.Abs(frame[row + x] - _previous[row + x]);
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

        _change.Add(whole / (255.0 * FrameBytes));
        _cutShare.Add(big / (double)(Grid * Grid));
        frame.CopyTo(_previous);
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
    public MotionSeries Series() => new(fps, [.. _luminance], [.. _change], [.. _cutShare]);
}
