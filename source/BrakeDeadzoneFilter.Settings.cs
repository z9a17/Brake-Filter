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

    [SliderProperty("Movement Anti-Chatter (default: 10)", 0f, MaximumMovementAntichatter, DefaultMovementAntichatter)]
    [DefaultPropertyValue(DefaultMovementAntichatter)]
    [Unit("raw units")]
    [ToolTip(
        "Reduces tiny movement and sideways jitter while keeping your main aim direction. " +
        "Higher is steadier but can make small corrections sticky; 0 disables it.")]
    public float MovementAntichatter
    {
        get => _movementAntichatter;
        set => _movementAntichatter = AimMath.ClampFinite(
            value, 0f, MaximumMovementAntichatter, DefaultMovementAntichatter);
    }

    [SliderProperty("Brake Strength (default: 0.45)", 0f, MaximumBrakeSmoothing, DefaultBrakeSmoothing)]
    [DefaultPropertyValue(DefaultBrakeSmoothing)]
    [Unit("ratio")]
    [ToolTip(
        "Steadies low-speed aim near a stop while fast movement passes through. " +
        "Higher gives stronger stops but heavier micro-aim; 0 disables it.")]
    public float BrakeSmoothing
    {
        get => _brakeSmoothing;
        set => _brakeSmoothing = AimMath.ClampFinite(
            value, 0f, MaximumBrakeSmoothing, DefaultBrakeSmoothing);
    }

    [SliderProperty("Brake Start Speed (default: 90)", MinimumBrakeSpeed, MaximumBrakeSpeed, DefaultBrakeSpeed)]
    [DefaultPropertyValue(DefaultBrakeSpeed)]
    [Unit("raw units/report")]
    [ToolTip(
        "Sets the speed below which Brake Strength fades in. " +
        "Higher affects faster movement; lower limits braking to slower aim.")]
    public float BrakeSpeed
    {
        get => _brakeSpeed;
        set => _brakeSpeed = AimMath.ClampFinite(
            value, MinimumBrakeSpeed, MaximumBrakeSpeed, DefaultBrakeSpeed);
    }

    [BooleanProperty(
        "Advanced Features (default: off)",
        "Enables endpoint Stop Assist and physical fast-aim stability. Advanced settings have no effect while off.")]
    [DefaultPropertyValue(false)]
    [ToolTip(
        "Enables endpoint hold, Stop Assist, and fast-aim stability. " +
        "Movement Anti-Chatter and Brake Strength still work while this is off.")]
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

    [SliderProperty("Advanced - Stability Radius (default: 0.05)", 0f, AdvancedAimEngine.MaximumStabilityRadius, AdvancedAimEngine.DefaultStabilityRadius)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultStabilityRadius)]
    [Unit("mm")]
    [ToolTip(
        "Holds a confirmed stopped endpoint inside this radius. " +
        "Higher contains more shake but can resist tiny corrections; requires Advanced Features.")]
    public float StabilityRadius
    {
        get => _advancedEngine.StabilityRadius;
        set => _advancedEngine.StabilityRadius = value;
    }

    [SliderProperty("Advanced - Stop Assist (default: 0.25)", 0f, AdvancedAimEngine.MaximumStopAssist, AdvancedAimEngine.DefaultStopAssist)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultStopAssist)]
    [Unit("ratio")]
    [ToolTip(
        "Adds a brief bounded brake when fast aim sharply slows near an endpoint. " +
        "Higher stops more strongly; 0 disables it and Advanced Features is required.")]
    public float StopAssist
    {
        get => _advancedEngine.StopAssist;
        set => _advancedEngine.StopAssist = value;
    }

    [SliderProperty("Advanced - Fast Aim Stability (default: 0.80)", 0f, MaximumFastAimStability, DefaultFastAimStability)]
    [DefaultPropertyValue(DefaultFastAimStability)]
    [Unit("ratio")]
    [ToolTip(
        "Adds sideways anti-chatter as physical aim speed rises. " +
        "Higher makes fast jumps straighter but can restrict curves; requires Advanced Features.")]
    public float FastAimStability
    {
        get => _fastAimStability;
        set => _fastAimStability = AimMath.ClampFinite(
            value, 0f, MaximumFastAimStability, DefaultFastAimStability);
    }

    [SliderProperty("Advanced - Fast Aim Threshold (default: 120)", AdvancedAimEngine.MinimumFastAimThreshold, AdvancedAimEngine.MaximumFastAimThreshold, AdvancedAimEngine.DefaultFastAimThreshold)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultFastAimThreshold)]
    [Unit("mm/s")]
    [ToolTip(
        "Sets when Fast Aim Stability reaches full strength and calibrates Stop Assist. " +
        "Lower engages sooner; higher reserves it for faster aim. Requires Advanced Features.")]
    public float FastAimThreshold
    {
        get => _advancedEngine.FastAimThreshold;
        set => _advancedEngine.FastAimThreshold = value;
    }
}
