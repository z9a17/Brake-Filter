namespace BrakeFilter;

public sealed partial class MotionStabilityProcessor
{
    public const float DefaultStabilityRadius = 0.05f;
    public const float DefaultEndpointBrake = 0.25f;
    public const float DefaultMotionSpeedThreshold = 120f;

    public const float MaximumStabilityRadius = 1f;
    public const float MaximumEndpointBrake = 1f;
    public const float MinimumMotionSpeedThreshold = 40f;
    public const float MaximumMotionSpeedThreshold = 5000f;

    private float _stabilityRadius = DefaultStabilityRadius;
    private float _endpointBrake = DefaultEndpointBrake;
    private float _motionSpeedThreshold = DefaultMotionSpeedThreshold;

    public float StabilityRadius
    {
        get => _stabilityRadius;
        set => _stabilityRadius = MotionMath.ClampFinite(
            value, 0f, MaximumStabilityRadius, DefaultStabilityRadius);
    }

    public float EndpointBrake
    {
        get => _endpointBrake;
        set => _endpointBrake = MotionMath.ClampFinite(
            value, 0f, MaximumEndpointBrake, DefaultEndpointBrake);
    }

    public float MotionSpeedThreshold
    {
        get => _motionSpeedThreshold;
        set => _motionSpeedThreshold = MotionMath.ClampFinite(
            value,
            MinimumMotionSpeedThreshold,
            MaximumMotionSpeedThreshold,
            DefaultMotionSpeedThreshold);
    }
}
