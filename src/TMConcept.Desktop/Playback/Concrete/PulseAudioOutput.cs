using System.Diagnostics;
using System.Runtime.InteropServices;
using TMConcept.Core.Playback;

namespace TMConcept.Desktop.Playback;

/// <summary>
/// Linux sound device: the PulseAudio "simple" API (libpulse-simple.so.0), which PipeWire serves too (pipewire-pulse), so it works on
/// both. ffmpeg decodes, this only plays PCM. <c>pa_simple_write</c> blocks while the server buffer (target 150 ms) is full. The played
/// position is written bytes − the server's latency, measured after every write on the sound thread (pa_simple is not used from two
/// threads) and moved on with the elapsed time in between.
/// </summary>
public sealed partial class PulseAudioOutput : IAudioOutput
{
    private const string Simple = "libpulse-simple.so.0";
    private const int PlaybackDirection = 1;
    private const int SampleS16Le = 3;
    private readonly Lock _lock = new();
    private IntPtr _handle;
    private double _bytesPerMs;
    private long _written;
    private long _playedAtMeasure;
    private long _measureStamp;

    /// <inheritdoc />
    public long PlayedBytes
    {
        get
        {
            lock (_lock)
            {
                if (_handle == IntPtr.Zero || _written == 0)
                {
                    return 0;
                }

                var since = Stopwatch.GetElapsedTime(_measureStamp).TotalMilliseconds;
                return Math.Min(_written, _playedAtMeasure + (long)(since * _bytesPerMs));
            }
        }
    }

    /// <inheritdoc />
    public void Open(int sampleRate, int channels)
    {
        _bytesPerMs = sampleRate * channels * 2 / 1000.0;
        var spec = new SampleSpec { Format = SampleS16Le, Rate = (uint)sampleRate, Channels = (byte)channels };
        var attributes = new BufferAttributes
        {
            MaxLength = uint.MaxValue,
            TargetLength = (uint)(_bytesPerMs * 150),
            PreBuffer = uint.MaxValue,
            MinimumRequest = uint.MaxValue,
            FragmentSize = uint.MaxValue,
        };
        try
        {
            _handle = SimpleNew(null, "TM Concept", PlaybackDirection, null, "Video", ref spec, IntPtr.Zero, ref attributes, out var error);
            if (_handle == IntPtr.Zero)
            {
                throw new AudioOutputException($"PulseAudio / PipeWire refused the sound stream (error {error}). Is a sound server running?");
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new AudioOutputException("libpulse-simple.so.0 is missing (PulseAudio or PipeWire's pulse server): install the package libpulse0.", ex);
        }
    }

    /// <inheritdoc />
    public unsafe void Write(byte[] buffer, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The device is not open.");
        }

        int error;
        fixed (byte* data = buffer)
        {
            if (SimpleWrite(_handle, data, (nuint)count, out error) < 0)
            {
                throw new AudioOutputException($"Writing to PulseAudio failed (error {error}).");
            }
        }

        var latencyUs = SimpleLatency(_handle, out error);
        lock (_lock)
        {
            _written += count;
            var latencyBytes = error == 0 ? (long)(latencyUs / 1000.0 * _bytesPerMs) : 0;
            _playedAtMeasure = Math.Max(_playedAtMeasure, Math.Max(0, _written - latencyBytes));
            _measureStamp = Stopwatch.GetTimestamp();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IntPtr handle;
        lock (_lock)
        {
            handle = _handle;
            _handle = IntPtr.Zero;
        }

        if (handle != IntPtr.Zero)
        {
            _ = SimpleFlush(handle, out _);
            SimpleFree(handle);
        }
    }

    [LibraryImport(Simple, EntryPoint = "pa_simple_new", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial IntPtr SimpleNew(string? server, string name, int direction, string? device, string streamName, ref SampleSpec spec,
        IntPtr channelMap, ref BufferAttributes attributes, out int error);

    [LibraryImport(Simple, EntryPoint = "pa_simple_write")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int SimpleWrite(IntPtr handle, byte* data, nuint bytes, out int error);

    [LibraryImport(Simple, EntryPoint = "pa_simple_get_latency")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial ulong SimpleLatency(IntPtr handle, out int error);

    [LibraryImport(Simple, EntryPoint = "pa_simple_flush")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int SimpleFlush(IntPtr handle, out int error);

    [LibraryImport(Simple, EntryPoint = "pa_simple_free")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial void SimpleFree(IntPtr handle);

    /// <summary>pa_sample_spec.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    /// <summary>pa_buffer_attr (uint.MaxValue = server default).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BufferAttributes
    {
        public uint MaxLength;
        public uint TargetLength;
        public uint PreBuffer;
        public uint MinimumRequest;
        public uint FragmentSize;
    }
}
