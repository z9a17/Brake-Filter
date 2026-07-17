using OpenTabletDriver.Plugin.Attributes;

namespace BrakeFilter;

public sealed partial class BrakeDeadzoneFilter
{
    private const float DefaultMovementAntichatter = 10f;
    private const float DefaultBrakeSmoothing = 0.45f;
    private const float DefaultBrakeSpeed = 90f;
    private const float DefaultFastMotionStability = 0.80f;

    private const float MaximumMovementAntichatter = 1000f;
    private const float MaximumBrakeSmoothing = 1f;
    private const float MinimumBrakeSpeed = 1f;
    private const float MaximumBrakeSpeed = 10000f;
    private const float MaximumFastMotionStability = 2f;

    private float _movementAntichatter = DefaultMovementAntichatter;
    private float _brakeSmoothing = DefaultBrakeSmoothing;
    private float _brakeSpeed = DefaultBrakeSpeed;
    private float _fastMotionStability = DefaultFastMotionStability;
    private bool _additionalStabilizationEnabled;

    private bool AdditionalStabilizationEnabled => _additionalStabilizationEnabled;
    private float FastMotionStability => _fastMotionStability;
    private float MotionSpeedThreshold => _stabilityProcessor.MotionSpeedThreshold;

    [SliderProperty("Movement Anti-Chatter", 0f, MaximumMovementAntichatter, DefaultMovementAntichatter)]
    [DefaultPropertyValue(DefaultMovementAntichatter)]
    [ToolTip(
        "Range: 0-1000 raw units; default: 10.\n\n" +
        "Creates a 360-degree radial deadzone around the filtered cursor. " +
        "The deadzone shrinks as per-report movement becomes faster.\n" +
        "Higher values suppress more shake but can make micro-corrections sticky. " +
        "0 disables Movement Anti-Chatter. The cursor-to-pen offset remains limited to this value.")]
    [Unit("raw units")]
    public float MovementAntichatter
    {
        get => _movementAntichatter;
        set => _movementAntichatter = MotionMath.ClampFinite(
            value, 0f, MaximumMovementAntichatter, DefaultMovementAntichatter);
    }

    [SliderProperty("Brake Strength", 0f, MaximumBrakeSmoothing, DefaultBrakeSmoothing)]
    [DefaultPropertyValue(DefaultBrakeSmoothing)]
    [ToolTip(
        "Range: 0.00-1.00; default: 0.45.\n\n" +
        "Controls how strongly slow movement is pulled toward the previous raw tablet position. " +
        "Braking fades out as speed approaches Brake Start Speed.\n" +
        "Higher values stop more firmly but make slow movement feel heavier. " +
        "0 disables normal braking; fast movement passes through.")]
    [Unit("ratio")]
    public float BrakeSmoothing
    {
        get => _brakeSmoothing;
        set => _brakeSmoothing = MotionMath.ClampFinite(
            value, 0f, MaximumBrakeSmoothing, DefaultBrakeSmoothing);
    }

    [SliderProperty("Brake Start Speed", MinimumBrakeSpeed, MaximumBrakeSpeed, DefaultBrakeSpeed)]
    [DefaultPropertyValue(DefaultBrakeSpeed)]
    [ToolTip(
        "Range: 1-10000 raw units/report; default: 90.\n\n" +
        "Sets the per-report speed where normal braking is fully released. " +
        "Braking is strongest below 35% of this value and smoothly fades to zero at it.\n" +
        "Higher values let braking affect faster movement; lower values confine it closer to a stop. " +
        "This does not change Brake Strength.")]
    [Unit("raw units/report")]
    public float BrakeSpeed
    {
        get => _brakeSpeed;
        set => _brakeSpeed = MotionMath.ClampFinite(
            value, MinimumBrakeSpeed, MaximumBrakeSpeed, DefaultBrakeSpeed);
    }

    [BooleanProperty(
        "Additional Stabilization",
        "Enables endpoint holding, endpoint braking, and fast-motion stabilization. Related settings have no effect while off.")]
    [DefaultPropertyValue(false)]
    [ToolTip(
        "Default: off.\n\n" +
        "Master switch for Stability Radius, Endpoint Brake, and Fast-Motion Stability. " +
        "Turning it off bypasses these features and clears their stored state.\n" +
        "Movement Anti-Chatter and normal Brake Strength remain active.")]
    // OTD serializes public property identifiers in saved profiles. Keep the
    // legacy identifier while presenting neutral terminology in the UI.
    public bool AdvancedFeatures
    {
        get => _additionalStabilizationEnabled;
        set
        {
            if (_additionalStabilizationEnabled == value)
            {
                return;
            }

            _additionalStabilizationEnabled = value;
            ClearAdditionalStabilizationState();
        }
    }

    [SliderProperty("Stability Radius", 0f, MotionStabilityProcessor.MaximumStabilityRadius, MotionStabilityProcessor.DefaultStabilityRadius)]
    [DefaultPropertyValue(MotionStabilityProcessor.DefaultStabilityRadius)]
    [ToolTip(
        "Range: 0.00-1.00 mm; default: 0.05 mm.\n\n" +
        "After a stationary endpoint is confirmed, holds the output inside this physical radius " +
        "to contain shake. It does not filter continuous movement.\n" +
        "Higher values allow a wider endpoint hold but can resist tiny restart corrections. " +
        "0 disables endpoint hold. Requires Additional Stabilization.")]
    [Unit("mm")]
    public float StabilityRadius
    {
        get => _stabilityProcessor.StabilityRadius;
        set => _stabilityProcessor.StabilityRadius = value;
    }

    [SliderProperty("Endpoint Brake", 0f, MotionStabilityProcessor.MaximumEndpointBrake, MotionStabilityProcessor.DefaultEndpointBrake)]
    [DefaultPropertyValue(MotionStabilityProcessor.DefaultEndpointBrake)]
    [ToolTip(
        "Range: 0.00-1.00; default: 0.25.\n\n" +
        "Applies a brief, bounded extra brake when a fast approach suddenly slows near an endpoint. " +
        "It does not predict targets or add a buffered frame.\n" +
        "Higher values stop more firmly but may feel grabby. " +
        "0 disables Endpoint Brake. Requires Additional Stabilization.")]
    [Unit("ratio")]
    public float StopAssist
    {
        get => _stabilityProcessor.EndpointBrake;
        set => _stabilityProcessor.EndpointBrake = value;
    }

    [SliderProperty("Fast-Motion Stability", 0f, MaximumFastMotionStability, DefaultFastMotionStability)]
    [DefaultPropertyValue(DefaultFastMotionStability)]
    [ToolTip(
        "Range: 0.00-2.00; default: 0.80.\n\n" +
        "As physical speed rises, keeps radial Anti-Chatter active farther into fast movement " +
        "without favoring any direction. It changes release speed, not the maximum positional leash.\n" +
        "Higher values provide more fast-motion stability but release later. " +
        "0 adds no fast-speed extension. Requires Additional Stabilization.")]
    [Unit("ratio")]
    public float FastAimStability
    {
        get => _fastMotionStability;
        set => _fastMotionStability = MotionMath.ClampFinite(
            value, 0f, MaximumFastMotionStability, DefaultFastMotionStability);
    }

    [SliderProperty("Motion-Speed Threshold", MotionStabilityProcessor.MinimumMotionSpeedThreshold, MotionStabilityProcessor.MaximumMotionSpeedThreshold, MotionStabilityProcessor.DefaultMotionSpeedThreshold)]
    [DefaultPropertyValue(MotionStabilityProcessor.DefaultMotionSpeedThreshold)]
    [ToolTip(
        "Range: 40-5000 mm/s; default: 120 mm/s.\n\n" +
        "Sets the physical pen speed where Fast-Motion Stability reaches full strength; " +
        "it starts blending at half this value. It also calibrates when Endpoint Brake recognizes a fast approach.\n" +
        "Lower values activate both sooner; higher values reserve them for faster movement. " +
        "Requires Additional Stabilization.")]
    [Unit("mm/s")]
    public float FastAimThreshold
    {
        get => _stabilityProcessor.MotionSpeedThreshold;
        set => _stabilityProcessor.MotionSpeedThreshold = value;
    }
}
