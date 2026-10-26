using System.Diagnostics;

namespace MDSM.Core;

// CPU submission costs, not GPU timings. No readbacks, file I/O or per-frame strings.
public sealed class FrameDiagnostics
{
    private long _updateAllocations;
    public double UpdateMilliseconds { get; private set; }
    public double DrawMilliseconds { get; private set; }
    public long AllocatedBytes { get; private set; }
    public int DrawCalls { get; set; }
    public long Triangles { get; set; }
    public int Mobs { get; set; }
    public int Items { get; set; }
    public int FallingBlocks { get; set; }
    public long DynamicBufferBytes { get; set; }
    public void Update(long start, long allocations)
    {
        UpdateMilliseconds += (Stopwatch.GetElapsedTime(start).TotalMilliseconds - UpdateMilliseconds) * .1;
        _updateAllocations = allocations;
    }
    public void Draw(long start, long allocations)
    {
        DrawMilliseconds += (Stopwatch.GetElapsedTime(start).TotalMilliseconds - DrawMilliseconds) * .1;
        AllocatedBytes = _updateAllocations + allocations;
    }
}
