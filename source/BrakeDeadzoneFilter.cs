using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace BrakeFilter;

/// <summary>
/// Suppresses small raw-position chatter and applies bounded, speed-sensitive
/// braking before OpenTabletDriver transforms tablet coordinates to pixels.
/// </summary>
[PluginName("Brake Filter")]
public sealed class BrakeDeadzoneFilter : IPositionedPipelineElement<IDeviceReport>
{
    private const float DefaultMovementAntichatter = 10f;
    private const float DefaultBrakeSmoothing = 0.45f;
    private const float DefaultBrakeSpeed = 90f;
    private const float DefaultFastAimStability = 0.80f;

    private const float MaximumMovementAntichatter = 100f;
    private const float MaximumBrakeSmoothing = 0.95f;
    private const float MinimumBrakeSpeed = 1f;
    private const float MaximumBrakeSpeed = 1000f;
    private const float MaximumFastAimStability = 1f;

    private const float ResetDistance = 5000f;
    private const float ResetDistanceSquared = ResetDistance * ResetDistance;
    private const float DirectionUpdateMinimumSpeed = 20f;
    private const float DirectionSmoothing = 0.35f;
    private const float SpeedSmoothing = 0.35f;
    private const float FullBrakeSpeedRatio = 0.35f;
    private const float MinimumAdvancedDeltaTime = 0.00025f;
    private const float MaximumAdvancedDeltaTime = 0.020f;
    private const float AdvancedResetTime = 0.050f;

    private bool _initialized;
    private readonly AdvancedAimEngine _advancedEngine = new();
    private Vector2 _previousRawPosition;
    private Vector2 _antichatterPosition;
    private Vector2 _movementDirection;
    private Vector2 _millimetresPerUnit = Vector2.One;
    private float _smoothedSpeed;
    private long _advancedLastTimestamp;
    private bool _advancedHasTimestamp;
    private bool _advancedFeatures;

    private float _movementAntichatter = DefaultMovementAntichatter;
    private float _brakeSmoothing = DefaultBrakeSmoothing;
    private float _brakeSpeed = DefaultBrakeSpeed;
    private float _fastAimStability = DefaultFastAimStability;

    public PipelinePosition Position => PipelinePosition.PreTransform;

    [SliderProperty("Movement Anti-Chatter (default: 10)", 0f, MaximumMovementAntichatter, DefaultMovementAntichatter)]
    [DefaultPropertyValue(DefaultMovementAntichatter)]
    [Unit("raw units")]
    [ToolTip(
        "DEFAULT: 10. RANGE: 0-100 raw units; 0 disables this stage.\n" +
        "Suggested start for a PTH-660 at 200 Hz: 6-18.\n\n" +
        "Suppresses tiny motion and sideways jitter while preserving the main movement direction.\n" +
        "Increase it if fast movement or straight strokes look shaky.\n" +
        "Decrease it if small corrections feel sticky or disappear.\n" +
        "Values above 25 are intentionally aggressive and can make tiny corrections feel stepped.")]
    public float MovementAntichatter
    {
        get => _movementAntichatter;
        set => _movementAntichatter = ClampFinite(
            value,
            minimum: 0f,
            maximum: MaximumMovementAntichatter,
            fallback: DefaultMovementAntichatter);
    }

    [SliderProperty("Brake Strength (default: 0.45)", 0f, MaximumBrakeSmoothing, DefaultBrakeSmoothing)]
    [DefaultPropertyValue(DefaultBrakeSmoothing)]
    [Unit("ratio")]
    [ToolTip(
        "DEFAULT: 0.45. RANGE: 0.00-0.95; 0 disables this stage.\n\n" +
        "Controls how strongly slow movement is steadied near the end of a stroke. " +
        "Fast movement is not brake-smoothed.\n" +
        "Increase it for steadier stops.\n" +
        "Decrease it if aim near a target feels heavy or delayed.\n" +
        "At 0.95, slow movement is pulled almost entirely toward the previous raw report.")]
    public float BrakeSmoothing
    {
        get => _brakeSmoothing;
        set => _brakeSmoothing = ClampFinite(
            value,
            minimum: 0f,
            maximum: MaximumBrakeSmoothing,
            fallback: DefaultBrakeSmoothing);
    }

    [SliderProperty("Brake Start Speed (default: 90)", MinimumBrakeSpeed, MaximumBrakeSpeed, DefaultBrakeSpeed)]
    [DefaultPropertyValue(DefaultBrakeSpeed)]
    [Unit("raw units/report")]
    [ToolTip(
        "DEFAULT: 90. RANGE: 1-1000 raw units per report.\n" +
        "Suggested start for a PTH-660 at 200 Hz: 60-140.\n\n" +
        "Braking is off at or above this speed and becomes stronger as the pen slows.\n" +
        "Increase it to engage braking earlier.\n" +
        "Decrease it to keep medium-speed movement more direct.\n" +
        "This value depends on tablet resolution and report rate.")]
    public float BrakeSpeed
    {
        get => _brakeSpeed;
        set => _brakeSpeed = ClampFinite(
            value,
            minimum: MinimumBrakeSpeed,
            maximum: MaximumBrakeSpeed,
            fallback: DefaultBrakeSpeed);
    }

    [BooleanProperty(
        "Advanced Features (default: off)",
        "Enables endpoint Stop Assist and physical fast-aim stability. The advanced settings below have no effect while this is off.")]
    [DefaultPropertyValue(false)]
    [ToolTip(
        "OFF BY DEFAULT. Turn this on before using Stop Assist, Stability Radius, Fast Aim Stability, or Fast Aim Threshold.\n" +
        "The normal Movement Anti-Chatter and Brake Strength stages remain active independently.")]
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
        "ADVANCED FEATURES MUST BE ON. Endpoint hold radius used only after the pen is detected as stationary.\n" +
        "It does not smooth continuous movement and does not duplicate Movement Anti-Chatter.\n" +
        "Increase it for steadier settled stops; decrease it if the endpoint hold releases too late.")]
    public float StabilityRadius
    {
        get => _advancedEngine.StabilityRadius;
        set => _advancedEngine.StabilityRadius = value;
    }

    [SliderProperty("Advanced - Stop Assist (default: 0.25)", 0f, AdvancedAimEngine.MaximumStopAssist, AdvancedAimEngine.DefaultStopAssist)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultStopAssist)]
    [Unit("ratio")]
    [ToolTip(
        "ADVANCED FEATURES MUST BE ON. Adds a short, non-recursive brake while a fast movement decelerates into its endpoint.\n" +
        "It releases on new movement and does not add a continuing cursor tail.")]
    public float StopAssist
    {
        get => _advancedEngine.StopAssist;
        set => _advancedEngine.StopAssist = value;
    }

    [SliderProperty("Advanced - Fast Aim Stability (default: 0.80)", 0f, MaximumFastAimStability, DefaultFastAimStability)]
    [DefaultPropertyValue(DefaultFastAimStability)]
    [Unit("ratio")]
    [ToolTip(
        "ADVANCED FEATURES MUST BE ON. Adds speed-scaled sideways strength inside the existing Movement Anti-Chatter stage.\n" +
        "It is not a second smoothing pass and cannot increase the normal stage's maximum positional offset.\n" +
        "Decrease it if curves feel constrained; increase it if fast jump lines shake sideways.")]
    public float FastAimStability
    {
        get => _fastAimStability;
        set => _fastAimStability = ClampFinite(
            value,
            minimum: 0f,
            maximum: MaximumFastAimStability,
            fallback: DefaultFastAimStability);
    }

    [SliderProperty("Advanced - Fast Aim Threshold (default: 120)", AdvancedAimEngine.MinimumFastAimThreshold, AdvancedAimEngine.MaximumFastAimThreshold, AdvancedAimEngine.DefaultFastAimThreshold)]
    [DefaultPropertyValue(AdvancedAimEngine.DefaultFastAimThreshold)]
    [Unit("mm/s")]
    [ToolTip(
        "ADVANCED FEATURES MUST BE ON. Speed where additional fast lateral stability reaches full strength.\n" +
        "It also calibrates which speed drops count as an approach for Stop Assist.\n" +
        "Lower it for a snappier response; raise it for more stability at medium speed.")]
    public float FastAimThreshold
    {
        get => _advancedEngine.FastAimThreshold;
        set => _advancedEngine.FastAimThreshold = value;
    }

    [TabletReference]
    public TabletReference TabletReference
    {
        set
        {
            DigitizerSpecifications? digitizer = value?.Properties?.Specifications?.Digitizer;
            if (digitizer is null)
            {
                // OTD may trigger tablet-reference properties while constructing a
                // filter before the profile has resolved its full tablet metadata.
                // Raw-unit scaling is safe until a complete reference is supplied.
                _millimetresPerUnit = Vector2.One;
                ClearState();
                return;
            }

            _millimetresPerUnit = new Vector2(
                SafeScale(digitizer.Width, digitizer.MaxX),
                SafeScale(digitizer.Height, digitizer.MaxY));
            ClearState();
        }
    }

    public event Action<IDeviceReport>? Emit;

    public void Consume(IDeviceReport report)
    {
        if (IsOutOfRange(report))
        {
            ClearState();
            Emit?.Invoke(report);
            return;
        }

        if (report is not ITabletReport tabletReport)
        {
            Emit?.Invoke(report);
            return;
        }

        Vector2 rawPosition = tabletReport.Position;
        if (!IsFinite(rawPosition))
        {
            ClearState();
            Emit?.Invoke(report);
            return;
        }

        if (!_initialized)
        {
            Reset(rawPosition);
            ResetAdvanced(rawPosition);
            Emit?.Invoke(report);
            return;
        }

        Vector2 rawDelta = rawPosition - _previousRawPosition;
        float distanceSquared = rawDelta.LengthSquared();
        if (!float.IsFinite(distanceSquared) || distanceSquared > ResetDistanceSquared)
        {
            Reset(rawPosition);
            ResetAdvanced(rawPosition);
            tabletReport.Position = rawPosition;
            Emit?.Invoke(report);
            return;
        }

        float speed = MathF.Sqrt(distanceSquared);
        float advancedDeltaTime = GetAdvancedDeltaTime(rawDelta, out float physicalSpeed);
        UpdateMovementDirection(rawDelta, speed);

        Vector2 antichatterDelta = ApplyMovementAntichatter(rawDelta, speed, physicalSpeed);
        Vector2 antichatterTarget = _antichatterPosition + antichatterDelta;
        antichatterTarget = LimitOffsetFromRaw(
            antichatterTarget,
            rawPosition,
            MovementAntichatter);

        _smoothedSpeed = Lerp(_smoothedSpeed, speed, SpeedSmoothing);

        // Current speed disables braking immediately on acceleration. The smoothed
        // value only delays re-engagement slightly while the pen decelerates.
        float brakeGateSpeed = MathF.Max(speed, _smoothedSpeed);
        float brakeAmount = BrakeSmoothing * GetSlowMovementFactor(brakeGateSpeed);

        // Anchor braking to the previous raw report instead of recursively feeding
        // the braked output back into itself. This gives a bounded one-report brake
        // and avoids the growing cursor lag present in v1.
        Vector2 outputPosition = brakeAmount > 0f
            ? Vector2.Lerp(antichatterTarget, _previousRawPosition, brakeAmount)
            : antichatterTarget;

        if (!IsFinite(outputPosition))
        {
            Reset(rawPosition);
            outputPosition = rawPosition;
        }
        else
        {
            _previousRawPosition = rawPosition;
            _antichatterPosition = antichatterTarget;
        }

        tabletReport.Position = ApplyAdvanced(outputPosition, advancedDeltaTime);
        Emit?.Invoke(report);
    }

    private static bool IsOutOfRange(IDeviceReport report)
    {
        // NearProximity=false is still a valid far-hover report on several Wacom
        // parsers, including the PTH-660's IntuosV2 parser. Only the driver's
        // explicit out-of-range report means the pen has actually left range.
        return report is OutOfRangeReport;
    }

    private void ClearState()
    {
        _initialized = false;
        _previousRawPosition = Vector2.Zero;
        _antichatterPosition = Vector2.Zero;
        _movementDirection = Vector2.Zero;
        _smoothedSpeed = 0f;
        ClearAdvancedState();
    }

    private void Reset(Vector2 rawPosition)
    {
        _previousRawPosition = rawPosition;
        _antichatterPosition = rawPosition;
        _movementDirection = Vector2.Zero;
        _smoothedSpeed = 0f;
        _initialized = true;
    }

    private Vector2 ApplyAdvanced(Vector2 basicPosition, float deltaTime)
    {
        if (!AdvancedFeatures)
        {
            return basicPosition;
        }

        Vector2 millimetrePosition = basicPosition * _millimetresPerUnit;
        Vector2 advancedPosition = _advancedEngine.Process(millimetrePosition, deltaTime);
        if (!IsFinite(advancedPosition))
        {
            ClearAdvancedState();
            return basicPosition;
        }

        return advancedPosition / _millimetresPerUnit;
    }

    private float GetAdvancedDeltaTime(Vector2 rawDelta, out float physicalSpeed)
    {
        physicalSpeed = 0f;
        if (!AdvancedFeatures)
        {
            return 0f;
        }

        long timestamp = Stopwatch.GetTimestamp();
        if (!_advancedHasTimestamp)
        {
            _advancedLastTimestamp = timestamp;
            _advancedHasTimestamp = true;
            return 0f;
        }

        float deltaTime = (float)((timestamp - _advancedLastTimestamp) / (double)Stopwatch.Frequency);
        _advancedLastTimestamp = timestamp;
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f || deltaTime > AdvancedResetTime)
        {
            return deltaTime;
        }

        Vector2 physicalDelta = rawDelta * _millimetresPerUnit;
        float distanceSquared = physicalDelta.LengthSquared();
        if (float.IsFinite(distanceSquared))
        {
            float speedTime = Math.Clamp(
                deltaTime,
                MinimumAdvancedDeltaTime,
                MaximumAdvancedDeltaTime);
            physicalSpeed = MathF.Sqrt(distanceSquared) / speedTime;
        }

        return deltaTime;
    }

    private void ResetAdvanced(Vector2 rawPosition)
    {
        ClearAdvancedState();
        if (!AdvancedFeatures)
        {
            return;
        }

        _advancedEngine.Reset(rawPosition * _millimetresPerUnit);
        _advancedLastTimestamp = Stopwatch.GetTimestamp();
        _advancedHasTimestamp = true;
    }

    private void ClearAdvancedState()
    {
        _advancedEngine.Clear();
        _advancedLastTimestamp = 0;
        _advancedHasTimestamp = false;
    }

    private void UpdateMovementDirection(Vector2 rawDelta, float speed)
    {
        if (speed <= DirectionUpdateMinimumSpeed)
        {
            return;
        }

        Vector2 currentDirection = rawDelta / speed;
        if (_movementDirection == Vector2.Zero)
        {
            _movementDirection = currentDirection;
            return;
        }

        Vector2 blendedDirection = Vector2.Lerp(
            _movementDirection,
            currentDirection,
            DirectionSmoothing);
        float blendedLengthSquared = blendedDirection.LengthSquared();

        if (float.IsFinite(blendedLengthSquared) && blendedLengthSquared > 1e-12f)
        {
            _movementDirection = blendedDirection / MathF.Sqrt(blendedLengthSquared);
        }
        else
        {
            _movementDirection = currentDirection;
        }
    }

    private Vector2 ApplyMovementAntichatter(
        Vector2 rawDelta,
        float speed,
        float physicalSpeed)
    {
        float deadzone = MovementAntichatter;
        if (deadzone <= 0f || speed <= 0f)
        {
            return rawDelta;
        }

        if (speed <= deadzone)
        {
            return Vector2.Zero;
        }

        // Directional filtering is intentionally independent of BrakeSpeed.
        // In v1, raising BrakeSpeed silently disabled medium-speed anti-chatter.
        if (_movementDirection == Vector2.Zero || speed < DirectionUpdateMinimumSpeed)
        {
            return rawDelta;
        }

        float forwardDistance = Vector2.Dot(rawDelta, _movementDirection);
        Vector2 forwardMovement = _movementDirection * forwardDistance;
        Vector2 sidewaysMovement = rawDelta - forwardMovement;
        float fastBlend = AdvancedFeatures
            ? SmoothStep(
                physicalSpeed,
                FastAimThreshold * 0.50f,
                FastAimThreshold)
            : 0f;
        float lateralDeadzone = deadzone * (1f + FastAimStability * fastBlend);

        return forwardMovement + ApplyVectorDeadzone(sidewaysMovement, lateralDeadzone);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 ApplyVectorDeadzone(Vector2 value, float deadzone)
    {
        if (deadzone <= 0f)
        {
            return value;
        }

        float lengthSquared = value.LengthSquared();
        if (lengthSquared <= deadzone * deadzone)
        {
            return Vector2.Zero;
        }

        float length = MathF.Sqrt(lengthSquared);
        return value * ((length - deadzone) / length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 LimitOffsetFromRaw(
        Vector2 target,
        Vector2 rawPosition,
        float maximumOffset)
    {
        if (maximumOffset <= 0f)
        {
            return rawPosition;
        }

        Vector2 offset = target - rawPosition;
        float offsetLengthSquared = offset.LengthSquared();
        float maximumOffsetSquared = maximumOffset * maximumOffset;
        if (offsetLengthSquared <= maximumOffsetSquared)
        {
            return target;
        }

        float offsetLength = MathF.Sqrt(offsetLengthSquared);
        return rawPosition + offset * (maximumOffset / offsetLength);
    }

    private float GetSlowMovementFactor(float speed)
    {
        float fullBrakeSpeed = BrakeSpeed * FullBrakeSpeedRatio;
        if (speed <= fullBrakeSpeed)
        {
            return 1f;
        }

        if (speed >= BrakeSpeed)
        {
            return 0f;
        }

        return 1f - SmoothStep(speed, fullBrakeSpeed, BrakeSpeed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SmoothStep(float value, float start, float end)
    {
        if (end <= start)
        {
            return value >= end ? 1f : 0f;
        }

        float amount = Math.Clamp((value - start) / (end - start), 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float start, float end, float amount)
    {
        return start + (end - start) * amount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ClampFinite(
        float value,
        float minimum,
        float maximum,
        float fallback)
    {
        return float.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SafeScale(float millimetres, float maximumRaw)
    {
        float scale = millimetres / maximumRaw;
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
