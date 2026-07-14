using System;
using System.Numerics;

namespace BrakeFilter;

/// <summary>
/// Allocation-free endpoint assistance operating in physical millimetres.
/// </summary>
public sealed partial class AdvancedAimEngine
{
    private const float MinimumDeltaTime = 0.00025f;
    private const float MaximumDeltaTime = 0.020f;
    private const float ResetTime = 0.050f;
    private const float PeakReleaseSeconds = 0.050f;
    private const float MaximumStopAssistOffset = 0.10f;

    private bool _initialized;
    private bool _settled;
    private Vector2 _previousInput;
    private Vector2 _holdAnchor;
    private Vector2 _output;
    private float _peakSpeed;

    public Vector2 Output => _output;
    public bool IsSettled => _settled;

    public Vector2 Process(Vector2 input, float deltaTimeSeconds)
    {
        if (!AimMath.IsFinite(input))
        {
            Clear();
            return input;
        }

        if (!_initialized ||
            !float.IsFinite(deltaTimeSeconds) ||
            deltaTimeSeconds <= 0f ||
            deltaTimeSeconds > ResetTime)
        {
            return Reset(input);
        }

        float deltaTime = Math.Clamp(deltaTimeSeconds, MinimumDeltaTime, MaximumDeltaTime);
        Vector2 inputDelta = input - _previousInput;
        float distanceSquared = inputDelta.LengthSquared();
        if (!float.IsFinite(distanceSquared))
        {
            return Reset(input);
        }

        float distance = MathF.Sqrt(distanceSquared);
        float speed = distance / deltaTime;
        float previousPeak = _peakSpeed;
        _peakSpeed = MathF.Max(speed, previousPeak * MathF.Exp(-deltaTime / PeakReleaseSeconds));

        float holdRadius = StabilityRadius * 1.33f;
        if (TryHoldSettledPosition(input, holdRadius, out Vector2 heldPosition))
        {
            return heldPosition;
        }

        if (UpdateStationaryCandidate(input, distance, speed, deltaTime, previousPeak, holdRadius))
        {
            return SettleAt(input);
        }

        float brakeAmount = CalculateStopAssist(speed, previousPeak);
        _output = brakeAmount > 0f
            ? Vector2.Lerp(input, _previousInput, brakeAmount)
            : input;
        _output = AimMath.LimitOffset(_output, input, MaximumStopAssistOffset);
        if (!AimMath.IsFinite(_output))
        {
            return Reset(input);
        }

        _previousInput = input;
        return _output;
    }

    private float CalculateStopAssist(float speed, float previousPeak)
    {
        float speedDrop = 1f - speed / MathF.Max(previousPeak, 1f);
        float dropFactor = AimMath.SmoothStep(speedDrop, 0.25f, 0.75f);
        float approachFactor = AimMath.SmoothStep(
            previousPeak,
            FastAimThreshold * 0.50f,
            FastAimThreshold);
        float endpointFactor = 1f - AimMath.SmoothStep(
            speed,
            FastAimThreshold * 0.29f,
            FastAimThreshold * 0.75f);

        return Math.Clamp(StopAssist * dropFactor * approachFactor * endpointFactor, 0f, 1f);
    }
}
