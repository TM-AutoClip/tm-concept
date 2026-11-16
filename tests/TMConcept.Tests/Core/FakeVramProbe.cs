using TMConcept.Core.Hardware;

namespace TMConcept.Tests.Core;

/// <summary>Graphics card memory from a script (the last reading repeats); counts the readings. Nothing native.</summary>
internal sealed class FakeVramProbe(params VramReading[] readings) : IVramProbe
{
    private readonly Queue<VramReading> _readings = new(readings);
    private VramReading _last = readings.Length > 0 ? readings[^1] : VramReading.None;

    public int Reads { get; private set; }

    public Task<VramReading> MeasureAsync(GpuAdapter? adapter, CancellationToken cancellationToken)
    {
        Reads++;
        if (_readings.Count > 0)
        {
            _last = _readings.Dequeue();
        }

        return Task.FromResult(_last);
    }
}