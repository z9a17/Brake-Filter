namespace BrakeFilter;

public sealed partial class AdvancedAimEngine
{
    public const float DefaultStabilityRadius = 0.05f;
    public const float DefaultStopAssist = 0.25f;
    public const float DefaultFastAimThreshold = 120f;

    public const float MaximumStabilityRadius = 1f;
    public const float MaximumStopAssist = 1f;
    public const float MinimumFastAimThreshold = 40f;
    public const float MaximumFastAimThreshold = 5000f;

    private float _stabilityRadius = DefaultStabilityRadius;
    private float _stopAssist = DefaultStopAssist;
    private float _fastAimThreshold = DefaultFastAimThreshold;

    public float StabilityRadius
    {
        get => _stabilityRadius;
        set => _stabilityRadius = AimMath.ClampFinite(
            value, 0f, MaximumStabilityRadius, DefaultStabilityRadius);
    }

    public float StopAssist
    {
        get => _stopAssist;
        set => _stopAssist = AimMath.ClampFinite(
            value, 0f, MaximumStopAssist, DefaultStopAssist);
    }

    public float FastAimThreshold
    {
        get => _fastAimThreshold;
        set => _fastAimThreshold = AimMath.ClampFinite(
            value,
            MinimumFastAimThreshold,
            MaximumFastAimThreshold,
            DefaultFastAimThreshold);
    }
}
