namespace TMConcept.Core.Playback;

/// <summary>
/// The player's clock in milliseconds of the video. With sound it follows the bytes the device has really played (so the picture waits
/// for a device that starts late, and never runs ahead of the voice); when the sound has ended and was played out it goes on with the
/// wall clock (a video longer than its sound does not freeze). Without sound it is the wall clock. <see cref="Freeze"/> stops it
/// (pause); a new <see cref="Start"/> begins at any position (play, seek). Thread-safe: the picture thread reads it.
/// </summary>
public sealed class PlaybackClock(TimeProvider time)
{
    private readonly Lock _lock = new();
    private long _startMs;
    private long _startStamp;
    private Func<long>? _played;
    private long? _soundEndBytes;
    private long _drainStamp;
    private bool _drained;
    private long? _frozenMs;

    /// <summary>Starts at <paramref name="startMs"/>; <paramref name="playedBytes"/> reads the device (null = no sound, wall clock).</summary>
    public void Start(long startMs, Func<long>? playedBytes)
    {
        lock (_lock)
        {
            _startMs = startMs;
            _startStamp = time.GetTimestamp();
            _played = playedBytes;
            _soundEndBytes = null;
            _drained = false;
            _frozenMs = null;
        }
    }

    /// <summary>The sound stream ended after <paramref name="totalBytes"/> bytes (from here on the clock may pass the sound).</summary>
    public void SoundEnded(long totalBytes)
    {
        lock (_lock)
        {
            _soundEndBytes = totalBytes;
        }
    }

    /// <summary>Stops the clock where it is and returns that position.</summary>
    public long Freeze()
    {
        lock (_lock)
        {
            _frozenMs ??= (long)Math.Round(Read());
            return _frozenMs.Value;
        }
    }

    /// <summary>Current position (ms of the video).</summary>
    public double PositionMs
    {
        get
        {
            lock (_lock)
            {
                return _frozenMs ?? Read();
            }
        }
    }

    private double Read()
    {
        var wall = time.GetElapsedTime(_startStamp).TotalMilliseconds;
        if (_played is not { } played)
        {
            return _startMs + wall;
        }

        var bytes = played();
        if (_soundEndBytes is { } end && bytes >= end)
        {
            if (!_drained)
            {
                _drained = true;
                _drainStamp = time.GetTimestamp();
            }

            return _startMs + (end / PlaybackFormat.BytesPerMs) + time.GetElapsedTime(_drainStamp).TotalMilliseconds;
        }

        return _startMs + (bytes / PlaybackFormat.BytesPerMs);
    }
}
