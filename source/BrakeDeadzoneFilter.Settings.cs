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
        "DEFAULT: 10. RANGE: 0-1000 raw units; 0 disables this stage.\n" +
        "Suggested start for a PTH-660 at 200 Hz: 6-18.\n\n" +
        "Suppresses tiny motion and sideways jitter while preserving the main movement direction.\n" +
        "Increase it if fast movement looks shaky; decrease it if corrections feel sticky.\n" +
        "Values above 25 are aggressive and can suppress intended corrections.")]
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
        "DEFAULT: 0.45. RANGE: 0.00-1.00; 0 disables this stage.\n\n" +
        "Steadies slow movement near the end of a stroke; fast movement bypasses it.\n" +
        "Increase it for steadier stops; decrease it if slow aim feels heavy.\n" +
        "At 1.00, fully braked movement is held to the previous raw report for one report.")]
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
        "DEFAULT: 90. RANGE: 1-10000 raw units per report.\n" +
        "Suggested start for a PTH-660 at 200 Hz: 60-140.\n\n" +
        "Braking is off at or above this speed and grows stronger as the pen slows.\n" +
        "High-resolution full-area tablets can exceed 2000 raw units per report.")]
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
        "OFF BY DEFAULT. Turn this on before using Stability Radius, Stop Assist, Fast Aim Stability, or Fast Aim Threshold.\n" +
        "Normal Movement Anti-Chatter and Brake Strength remain independent.")]
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
        "ADVANCED FEATURES MUST BE ON. Holds only a confirmed stationary endpoint; it does not smooth continuous movement.\n" +
        "Increase it for steadier settled stops. Values above 0.20 mm are aggressive.")]
    public float StabilityRadius
    {
        get => _advancedEngine.StabilityRadius;
        set => _advancedEngine.StabilityRadius = value;
    }

    [SliderProperty("Advanced - Stop Assist (default: 0.25)", 0f, AdvancedAimEngine.MaximumStopAssist, AdvancedAimEngine.DefaultStopAssist)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultStopAssist)]
    [Unit("ratio")]
    [ToolTip(
        "ADVANCED FEATURES MUST BE ON. Adds a short non-recursive brake during strong endpoint deceleration.\n" +
        "It releases on new movement and remains limited to 0.10 mm from input.")]
    public float StopAssist
    {
        get => _advancedEngine.StopAssist;
        set => _advancedEngine.StopAssist = value;
    }

    [SliderProperty("Advanced - Fast Aim Stability (default: 0.80)", 0f, MaximumFastAimStability, DefaultFastAimStability)]
    [DefaultPropertyValue(DefaultFastAimStability)]
    [Unit("ratio")]
    [ToolTip(
        "ADVANCED FEATURES MUST BE ON. Adds speed-scaled sideways strength inside Movement Anti-Chatter.\n" +
        "It cannot increase that stage's positional leash. Values above 1.00 are aggressive.")]
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
        "ADVANCED FEATURES MUST BE ON. Speed where extra lateral stability reaches full strength.\n" +
        "It also calibrates Stop Assist. Raise it to reserve full stability for faster movement.")]
    public float FastAimThreshold
    {
        get => _advancedEngine.FastAimThreshold;
        set => _advancedEngine.FastAimThreshold = value;
    }
}
