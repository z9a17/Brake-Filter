using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace BrakeFilter.RadialDev;

/// <summary>
/// Experimental low-latency radial stabilization for OpenTabletDriver.
/// </summary>
[PluginName("Brake Filter - Low-Latency Radial Dev")]
public sealed partial class LowLatencyRadialFilter : IPositionedPipelineElement<IDeviceReport>
{
    private readonly LowLatencyRadialProcessor _processor = new();
    private Vector2 _millimetresPerUnit = Vector2.One;

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    [TabletReference]
    public TabletReference TabletReference
    {
        set
        {
            DigitizerSpecifications? digitizer = value?.Properties?.Specifications?.Digitizer;
            _millimetresPerUnit = digitizer is null
                ? Vector2.One
                : new Vector2(
                    RadialMath.SafeScale(digitizer.Width, digitizer.MaxX),
                    RadialMath.SafeScale(digitizer.Height, digitizer.MaxY));
            _processor.Clear();
        }
    }

    public void Consume(IDeviceReport report)
    {
        if (report is OutOfRangeReport)
        {
            _processor.Clear();
            Emit?.Invoke(report);
            return;
        }

        if (report is not ITabletReport tabletReport)
        {
            Emit?.Invoke(report);
            return;
        }

        Vector2 rawPosition = tabletReport.Position;
        if (!RadialMath.IsFinite(rawPosition))
        {
            _processor.Clear();
            Emit?.Invoke(report);
            return;
        }

        Vector2 physicalPosition = rawPosition * _millimetresPerUnit;
        Vector2 filteredPosition = _processor.Process(physicalPosition);
        tabletReport.Position = RadialMath.IsFinite(filteredPosition)
            ? filteredPosition / _millimetresPerUnit
            : rawPosition;
        Emit?.Invoke(report);
    }
}
