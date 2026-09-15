using System.Diagnostics.Metrics;

namespace Reconstructed4021.Panemonde;

// Exposes frame timing as a real dotnet metric (visible via `dotnet-counters monitor -n <process>
// --counters Reconstructed4021.Panemonde`, which reports the histogram's mean/percentiles for free)
// instead of a bespoke rolling-average field nobody outside the process can observe.
internal static class FrameMetrics
{
    private static readonly Meter Meter = new("Reconstructed4021.Panemonde");
    private static readonly Histogram<double> FrameTime = Meter.CreateHistogram<double>(
        "panemonde.frame_time", unit: "ms", description: "Time to drain input, update, draw, and present one frame.");

    public static void Record(double milliseconds) => FrameTime.Record(milliseconds);
}
