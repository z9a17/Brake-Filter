using OpenTabletDriver.Plugin.Attributes;

namespace BrakeFilter.RadialDev;

public sealed partial class LowLatencyRadialFilter
{
    [SliderProperty("Outer Radius", 0f, LowLatencyRadialProcessor.MaximumRadius, LowLatencyRadialProcessor.DefaultOuterRadius)]
    [DefaultPropertyValue(LowLatencyRadialProcessor.DefaultOuterRadius)]
    [ToolTip("Range: 0.0000-5.0000 mm; default: 0.7039 mm.\n\nMaximum slow-movement lag radius. Higher values smooth more but feel farther behind. Fast Release reduces this radius during quick movement.")]
    [Unit("mm")]
    public float OuterRadius
    {
        get => _processor.OuterRadius;
        set => _processor.OuterRadius = value;
    }

    [SliderProperty("Inner Radius", 0f, LowLatencyRadialProcessor.MaximumRadius, LowLatencyRadialProcessor.DefaultInnerRadius)]
    [DefaultPropertyValue(LowLatencyRadialProcessor.DefaultInnerRadius)]
    [ToolTip("Range: 0.000-5.000 mm; default: 0.302 mm.\n\nMovement inside this radius is held as chatter. Higher values are steadier but resist micro-corrections. Values above Outer Radius are treated as equal to it.")]
    [Unit("mm")]
    public float InnerRadius
    {
        get => _processor.InnerRadius;
        set => _processor.InnerRadius = value;
    }

    [SliderProperty("Smoothing", 0f, 1f, LowLatencyRadialProcessor.DefaultSmoothing)]
    [DefaultPropertyValue(LowLatencyRadialProcessor.DefaultSmoothing)]
    [ToolTip("Range: 0.000-1.000; default: 0.302.\n\nControls slow-movement follow strength. Higher values catch up more slowly and feel smoother; lower values follow more directly.")]
    [Unit("ratio")]
    public float Smoothing
    {
        get => _processor.Smoothing;
        set => _processor.Smoothing = value;
    }

    [SliderProperty("Soft Knee", 0f, LowLatencyRadialProcessor.MaximumSoftKnee, LowLatencyRadialProcessor.DefaultSoftKnee)]
    [DefaultPropertyValue(LowLatencyRadialProcessor.DefaultSoftKnee)]
    [ToolTip("Range: 0.000-10.000; default: 0.603.\n\nControls how gradually fast release blends in. Higher values make the transition softer; lower values make it more decisive.")]
    [Unit("ratio")]
    public float SoftKnee
    {
        get => _processor.SoftKnee;
        set => _processor.SoftKnee = value;
    }

    [SliderProperty("Smoothing Leak", 0f, 1f, LowLatencyRadialProcessor.DefaultSmoothingLeak)]
    [DefaultPropertyValue(LowLatencyRadialProcessor.DefaultSmoothingLeak)]
    [ToolTip("Range: 0.000-1.000; default: 0.201.\n\nPreserves some smoothing during fast release. Higher values keep more stability at speed but retain more lag.")]
    [Unit("ratio")]
    public float SmoothingLeak
    {
        get => _processor.SmoothingLeak;
        set => _processor.SmoothingLeak = value;
    }

    [SliderProperty("Fast Release", 0f, 1f, LowLatencyRadialProcessor.DefaultFastRelease)]
    [DefaultPropertyValue(LowLatencyRadialProcessor.DefaultFastRelease)]
    [ToolTip("Range: 0.00-1.00; default: 0.75.\n\nShrinks smoothing lag as per-report movement increases. Higher values make jumps more direct. Direction reversals always release independently.")]
    [Unit("ratio")]
    public float FastRelease
    {
        get => _processor.FastRelease;
        set => _processor.FastRelease = value;
    }
}
