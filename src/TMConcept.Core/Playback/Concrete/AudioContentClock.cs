namespace TMConcept.Core.Playback;

/// <summary>
/// For a device that never stops (it plays silence while no sound is queued, like WaveOut): turns the bytes the device has played into
/// bytes of real sound. Every read the device makes is recorded as so many real bytes followed by so much padding; played sound = played
/// bytes − the padding inside them. Without this the picture is drawn about 300 ms before its sound (the silence at the start counts as
/// time).
/// </summary>
public sealed class AudioContentClock
{
    private readonly Lock _lock = new();
    private readonly List<(long Start, long Length)> _paddings = [];
    private long _given;

    /// <summary>The device read <paramref name="real"/> bytes of sound followed by <paramref name="padding"/> bytes of silence.</summary>
    public void Given(int real, int padding)
    {
        lock (_lock)
        {
            _given += real;
            if (padding <= 0)
            {
                return;
            }

            if (_paddings.Count > 0 && _paddings[^1].Start + _paddings[^1].Length == _given)
            {
                _paddings[^1] = (_paddings[^1].Start, _paddings[^1].Length + padding);
            }
            else
            {
                _paddings.Add((_given, padding));
            }

            _given += padding;
        }
    }

    /// <summary>Real sound bytes played when the device has played <paramref name="deviceBytes"/> bytes.</summary>
    public long ContentBytes(long deviceBytes)
    {
        lock (_lock)
        {
            long subtract = 0;
            foreach (var (start, length) in _paddings)
            {
                if (start >= deviceBytes)
                {
                    break;
                }

                subtract += Math.Min(length, deviceBytes - start);
            }

            return Math.Max(0, deviceBytes - subtract);
        }
    }
}
