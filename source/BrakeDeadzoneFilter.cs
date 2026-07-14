using System;
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
[PluginName("Brake Filter v0.1")]
public sealed class BrakeDeadzoneFilter : IPositionedPipelineElement<IDeviceReport>
{
    private const float DefaultMovementAntichatter = 10f;
    private const float DefaultBrakeSmoothing = 0.45f;
    private const float DefaultBrakeSpeed = 90f;

    private const float MaximumMovementAntichatter = 100f;
    private const float MaximumBrakeSmoothing = 0.95f;
    private const float MinimumBrakeSpeed = 1f;
    private const float MaximumBrakeSpeed = 1000f;

    private const float ResetDistance = 5000f;
    private const float ResetDistanceSquared = ResetDistance * ResetDistance;
    private const float DirectionUpdateMinimumSpeed = 20f;
    private const float DirectionSmoothing = 0.35f;
    private const float SpeedSmoothing = 0.35f;
    private const float FullBrakeSpeedRatio = 0.35f;

    private bool _initialized;
    private Vector2 _previousRawPosition;
    private Vector2 _antichatterPosition;
    private Vector2 _movementDirection;
    private float _smoothedSpeed;

    private float _movementAntichatter = DefaultMovementAntichatter;
    private float _brakeSmoothing = DefaultBrakeSmoothing;
    private float _brakeSpeed = DefaultBrakeSpeed;

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
            Emit?.Invoke(report);
            return;
        }

        Vector2 rawDelta = rawPosition - _previousRawPosition;
        float distanceSquared = rawDelta.LengthSquared();
        if (!float.IsFinite(distanceSquared) || distanceSquared > ResetDistanceSquared)
        {
            Reset(rawPosition);
            tabletReport.Position = rawPosition;
            Emit?.Invoke(report);
            return;
        }

        float speed = MathF.Sqrt(distanceSquared);
        UpdateMovementDirection(rawDelta, speed);

        Vector2 antichatterDelta = ApplyMovementAntichatter(rawDelta, speed);
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

        tabletReport.Position = outputPosition;
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
    }

    private void Reset(Vector2 rawPosition)
    {
        _previousRawPosition = rawPosition;
        _antichatterPosition = rawPosition;
        _movementDirection = Vector2.Zero;
        _smoothedSpeed = 0f;
        _initialized = true;
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

    private Vector2 ApplyMovementAntichatter(Vector2 rawDelta, float speed)
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

        return forwardMovement + ApplyVectorDeadzone(sidewaysMovement, deadzone);
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
    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
