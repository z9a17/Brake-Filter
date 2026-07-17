using OpenTabletDriver.Plugin.Attributes;

namespace BrakeFilter;

public sealed partial class BrakeDeadzoneFilter
{
    private const float DefaultMovementAntichatter = 10f;
    private const float DefaultBrakeSmoothing = 0.45f;
    private const float DefaultBrakeSpeed = 90f;
    private const float DefaultFastAimStability = 0.80f;

    private const float MaximumMovementAntichatter = 1000f;
    private const float MaximumBrakeSmoothing = 1f;
    private const float MinimumBrakeSpeed = 1f;
    private const float MaximumBrakeSpeed = 10000f;
    private const float MaximumFastAimStability = 2f;

    private float _movementAntichatter = DefaultMovementAntichatter;
    private float _brakeSmoothing = DefaultBrakeSmoothing;
    private float _brakeSpeed = DefaultBrakeSpeed;
    private float _fastAimStability = DefaultFastAimStability;
    private bool _advancedFeatures;

    [SliderProperty("Movement Anti-Chatter", 0f, MaximumMovementAntichatter, DefaultMovementAntichatter)]
    [DefaultPropertyValue(DefaultMovementAntichatter)]
    [ToolTip(
        "Range: 0-1000 raw units; default: 10.\n\n" +
        "Creates a direction-relative deadzone. Movement no larger than this value can be held, " +
        "while deviations perpendicular to your recent aim path are reduced.\n" +
        "Higher values suppress more shake but make micro-corrections and sharp turns stickier. " +
        "0 disables Movement Anti-Chatter. The cursor-to-pen offset remains limited to this value.")]
    [Unit("raw units")]
    public float MovementAntichatter
    {
        get => _movementAntichatter;
        set => _movementAntichatter = AimMath.ClampFinite(
            value, 0f, MaximumMovementAntichatter, DefaultMovementAntichatter);
    }

    [SliderProperty("Brake Strength", 0f, MaximumBrakeSmoothing, DefaultBrakeSmoothing)]
    [DefaultPropertyValue(DefaultBrakeSmoothing)]
    [ToolTip(
        "Range: 0.00-1.00; default: 0.45.\n\n" +
        "Controls how strongly slow aim is pulled toward the previous raw tablet position. " +
        "Braking fades out as speed approaches Brake Start Speed.\n" +
        "Higher values stop more firmly but make slow aim feel heavier. " +
        "0 disables normal braking; fast movement passes through.")]
    [Unit("ratio")]
    public float BrakeSmoothing
    {
        get => _brakeSmoothing;
        set => _brakeSmoothing = AimMath.ClampFinite(
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
        set => _brakeSpeed = AimMath.ClampFinite(
            value, MinimumBrakeSpeed, MaximumBrakeSpeed, DefaultBrakeSpeed);
    }

    [BooleanProperty(
        "Advanced Features",
        "Enables endpoint Stop Assist and physical fast-aim stability. Advanced settings have no effect while off.")]
    [DefaultPropertyValue(false)]
    [ToolTip(
        "Default: off.\n\n" +
        "Master switch for Stability Radius, Stop Assist, and Fast Aim Stability. " +
        "Turning it off bypasses these features and clears their stored state.\n" +
        "Movement Anti-Chatter and normal Brake Strength remain active.")]
    public bool AdvancedFeatures
    {
        get => _advancedFeatures;
        set
        {
            if (_advancedFeatures == value)
            {
                return;
            }

            _advancedFeatures = value;
            ClearAdvancedState();
        }
    }

    [SliderProperty("Stability Radius", 0f, AdvancedAimEngine.MaximumStabilityRadius, AdvancedAimEngine.DefaultStabilityRadius)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultStabilityRadius)]
    [ToolTip(
        "Range: 0.00-1.00 mm; default: 0.05 mm.\n\n" +
        "After a stationary endpoint is confirmed, holds the output inside this physical radius " +
        "to contain shake. It does not filter continuous movement.\n" +
        "Higher values allow a wider endpoint hold but can resist tiny restart corrections. " +
        "0 disables endpoint hold. Requires Advanced Features.")]
    [Unit("mm")]
    public float StabilityRadius
    {
        get => _advancedEngine.StabilityRadius;
        set => _advancedEngine.StabilityRadius = value;
    }

    [SliderProperty("Stop Assist", 0f, AdvancedAimEngine.MaximumStopAssist, AdvancedAimEngine.DefaultStopAssist)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultStopAssist)]
    [ToolTip(
        "Range: 0.00-1.00; default: 0.25.\n\n" +
        "Applies a brief, bounded extra brake when a fast approach suddenly slows near an endpoint. " +
        "It does not predict targets or add a buffered frame.\n" +
        "Higher values stop more firmly but may feel grabby. " +
        "0 disables Stop Assist. Requires Advanced Features.")]
    [Unit("ratio")]
    public float StopAssist
    {
        get => _advancedEngine.StopAssist;
        set => _advancedEngine.StopAssist = value;
    }

    [SliderProperty("Fast Aim Stability", 0f, MaximumFastAimStability, DefaultFastAimStability)]
    [DefaultPropertyValue(DefaultFastAimStability)]
    [ToolTip(
        "Range: 0.00-2.00; default: 0.80.\n\n" +
        "As physical speed rises, strengthens Anti-Chatter only perpendicular to your recent movement path. " +
        "This is relative to your trajectory, not screen-horizontal.\n" +
        "Higher values straighten fast jumps but can resist curves and sudden turns. " +
        "0 adds no fast-speed boost. Requires Advanced Features.")]
    [Unit("ratio")]
    public float FastAimStability
    {
        get => _fastAimStability;
        set => _fastAimStability = AimMath.ClampFinite(
            value, 0f, MaximumFastAimStability, DefaultFastAimStability);
    }

    [SliderProperty("Fast Aim Threshold", AdvancedAimEngine.MinimumFastAimThreshold, AdvancedAimEngine.MaximumFastAimThreshold, AdvancedAimEngine.DefaultFastAimThreshold)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultFastAimThreshold)]
    [ToolTip(
        "Range: 40-5000 mm/s; default: 120 mm/s.\n\n" +
        "Sets the physical aim speed where Fast Aim Stability reaches full strength; " +
        "it starts blending at half this value. It also calibrates when Stop Assist recognizes a fast approach.\n" +
        "Lower values activate both sooner; higher values reserve them for faster aim. " +
        "Requires Advanced Features.")]
    [Unit("mm/s")]
    public float FastAimThreshold
    {
        get => _advancedEngine.FastAimThreshold;
        set => _advancedEngine.FastAimThreshold = value;
    }
}
