using System;
using System.Numerics;

namespace BrakeFilter;

/// <summary>
/// Allocation-free endpoint and motion stabilization operating in physical millimetres.
/// </summary>
public sealed partial class MotionStabilityProcessor
{
    private const float MinimumDeltaTime = 0.00025f;
    private const float MaximumDeltaTime = 0.020f;
    private const float ResetTime = 0.050f;
    private const float PeakReleaseSeconds = 0.050f;
    private const float MaximumEndpointBrakeOffset = 0.10f;
    private const float HoldRadiusScale = 1.33f;
    private const float EndpointBrakeDropStart = 0.25f;
    private const float EndpointBrakeDropEnd = 0.75f;
    private const float EndpointBrakeApproachStartRatio = 0.50f;
    private const float EndpointBrakeStartRatio = 0.29f;
    private const float EndpointBrakeEndRatio = 0.75f;

    private bool _initialized;
    private bool _settled;
    private Vector2 _previousInput;
    private Vector2 _holdAnchor;
    private Vector2 _output;
    private float _peakSpeed;

    public Vector2 Output => _output;
    public bool IsSettled => _settled;

    public Vector2 Process(Vector2 input, float reportPeriodSeconds)
    {
        if (!MotionMath.IsFinite(input))
        {
            Clear();
            return input;
        }

        if (!_initialized ||
            !float.IsFinite(reportPeriodSeconds) ||
            reportPeriodSeconds <= 0f ||
            reportPeriodSeconds > ResetTime)
        {
            return Reset(input);
        }

        float reportPeriod = Math.Clamp(reportPeriodSeconds, MinimumDeltaTime, MaximumDeltaTime);
        Vector2 inputDelta = input - _previousInput;
        float distanceSquared = inputDelta.LengthSquared();
        if (!float.IsFinite(distanceSquared))
        {
            return Reset(input);
        }

        float distance = MathF.Sqrt(distanceSquared);
        float speed = distance / reportPeriod;
        return ProcessCore(input, reportPeriod, distance, speed, true);
    }

    internal Vector2 Process(
        Vector2 input,
        float reportPeriodSeconds,
        float motionSpeed,
        bool hasMotionSample)
    {
        if (!MotionMath.IsFinite(input))
        {
            Clear();
            return input;
        }

        if (!_initialized ||
            !float.IsFinite(reportPeriodSeconds) ||
            reportPeriodSeconds <= 0f ||
            reportPeriodSeconds > ResetTime ||
            !float.IsFinite(motionSpeed) ||
            motionSpeed < 0f)
        {
            return Reset(input);
        }

        float reportPeriod = Math.Clamp(reportPeriodSeconds, MinimumDeltaTime, MaximumDeltaTime);
        Vector2 inputDelta = input - _previousInput;
        float distanceSquared = inputDelta.LengthSquared();
        if (!float.IsFinite(distanceSquared))
        {
            return Reset(input);
        }

        return ProcessCore(
            input,
            reportPeriod,
            MathF.Sqrt(distanceSquared),
            motionSpeed,
            hasMotionSample);
    }

    private Vector2 ProcessCore(
        Vector2 input,
        float reportPeriod,
        float distance,
        float speed,
        bool hasMotionSample)
    {
        float previousPeak = _peakSpeed;
        float decayedPeak = previousPeak * MathF.Exp(-reportPeriod / PeakReleaseSeconds);
        _peakSpeed = hasMotionSample
            ? MathF.Max(speed, decayedPeak)
            : decayedPeak;

        bool needsSpeedDrop = hasMotionSample && (StabilityRadius > 0f || EndpointBrake > 0f);
        float speedDrop = needsSpeedDrop
            ? 1f - speed / MathF.Max(previousPeak, 1f)
            : 0f;

        float holdRadius = StabilityRadius * HoldRadiusScale;
        if (TryHoldSettledPosition(input, holdRadius, out Vector2 heldPosition))
        {
            return heldPosition;
        }

        if (UpdateStationaryWindow(
            input,
            distance,
            speed,
            speedDrop,
            reportPeriod,
            previousPeak,
            holdRadius,
            hasMotionSample))
        {
            return SettleAt(input);
        }

        float brakeAmount = hasMotionSample && EndpointBrake > 0f
            ? CalculateEndpointBrake(speed, previousPeak, speedDrop)
            : 0f;
        _output = brakeAmount > 0f
            ? MotionMath.LimitOffset(
                Vector2.Lerp(input, _previousInput, brakeAmount),
                input,
                MaximumEndpointBrakeOffset)
            : input;
        if (!MotionMath.IsFinite(_output))
        {
            return Reset(input);
        }

        _previousInput = input;
        return _output;
    }

    private float CalculateEndpointBrake(float speed, float previousPeak, float speedDrop)
    {
        float dropFactor = MotionMath.SmoothStep(
            speedDrop,
            EndpointBrakeDropStart,
            EndpointBrakeDropEnd);
        float approachFactor = MotionMath.SmoothStep(
            previousPeak,
            MotionSpeedThreshold * EndpointBrakeApproachStartRatio,
            MotionSpeedThreshold);
        float endpointFactor = 1f - MotionMath.SmoothStep(
            speed,
            MotionSpeedThreshold * EndpointBrakeStartRatio,
            MotionSpeedThreshold * EndpointBrakeEndRatio);

        return Math.Clamp(EndpointBrake * dropFactor * approachFactor * endpointFactor, 0f, 1f);
    }
}
