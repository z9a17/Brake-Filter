using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BrakeFilter;

/// <summary>
/// Allocation-free endpoint-only assistance. Input positions are already
/// anti-chattered by the normal stage and are expressed in millimetres.
/// StabilityRadius never filters continuous movement; it is used only to
/// recognize and hold an actual stationary endpoint.
/// </summary>
public sealed class AdvancedAimEngine
{
    public const float DefaultStabilityRadius = 0.05f;
    public const float DefaultStopAssist = 0.25f;
    public const float DefaultFastAimThreshold = 120f;

    public const float MaximumStabilityRadius = 0.20f;
    public const float MaximumStopAssist = 0.50f;
    public const float MinimumFastAimThreshold = 40f;
    public const float MaximumFastAimThreshold = 500f;

    private const float MinimumDeltaTime = 0.00025f;
    private const float MaximumDeltaTime = 0.020f;
    private const float ResetTime = 0.050f;
    private const float PeakReleaseSeconds = 0.050f;
    private const float StopCandidateSpeed = 60f;
    private const float StopDwellSeconds = 0.010f;
    private const float StopWindowSeconds = 0.030f;
    private const float StationaryCoherence = 0.55f;
    private const float MaximumBrakeAmount = 0.50f;
    private const float MaximumStopAssistOffset = 0.10f;

    private bool _initialized;
    private bool _settled;
    private bool _stationaryCandidate;
    private bool _wasAboveStopCandidateSpeed;
    private int _stationarySamples;
    private float _stationarySeconds;
    private float _stationaryPath;
    private Vector2 _stationaryOrigin;
    private Vector2 _previousInput;
    private Vector2 _holdAnchor;
    private Vector2 _output;
    private float _peakSpeed;

    private float _stabilityRadius = DefaultStabilityRadius;
    private float _stopAssist = DefaultStopAssist;
    private float _fastAimThreshold = DefaultFastAimThreshold;

    public float StabilityRadius
    {
        get => _stabilityRadius;
        set => _stabilityRadius = ClampFinite(value, 0f, MaximumStabilityRadius, DefaultStabilityRadius);
    }

    public float StopAssist
    {
        get => _stopAssist;
        set => _stopAssist = ClampFinite(value, 0f, MaximumStopAssist, DefaultStopAssist);
    }

    public float FastAimThreshold
    {
        get => _fastAimThreshold;
        set => _fastAimThreshold = ClampFinite(
            value,
            MinimumFastAimThreshold,
            MaximumFastAimThreshold,
            DefaultFastAimThreshold);
    }

    public Vector2 Output => _output;
    public bool IsSettled => _settled;

    public void Clear()
    {
        _initialized = false;
        _settled = false;
        _wasAboveStopCandidateSpeed = false;
        ResetStationaryCandidate();
        _previousInput = Vector2.Zero;
        _holdAnchor = Vector2.Zero;
        _output = Vector2.Zero;
        _peakSpeed = 0f;
    }

    public Vector2 Reset(Vector2 inputPosition)
    {
        if (!IsFinite(inputPosition))
        {
            Clear();
            return inputPosition;
        }

        _initialized = true;
        _settled = false;
        _wasAboveStopCandidateSpeed = false;
        ResetStationaryCandidate();
        _previousInput = inputPosition;
        _holdAnchor = inputPosition;
        _output = inputPosition;
        _peakSpeed = 0f;
        return inputPosition;
    }

    public Vector2 Process(Vector2 inputPosition, float deltaTimeSeconds)
    {
        if (!IsFinite(inputPosition))
        {
            Clear();
            return inputPosition;
        }

        if (!_initialized ||
            !float.IsFinite(deltaTimeSeconds) ||
            deltaTimeSeconds <= 0f ||
            deltaTimeSeconds > ResetTime)
        {
            return Reset(inputPosition);
        }

        float dt = Math.Clamp(deltaTimeSeconds, MinimumDeltaTime, MaximumDeltaTime);
        Vector2 inputDelta = inputPosition - _previousInput;
        float distanceSquared = inputDelta.LengthSquared();
        if (!float.IsFinite(distanceSquared))
        {
            return Reset(inputPosition);
        }

        float distance = MathF.Sqrt(distanceSquared);
        float speed = distance / dt;
        float previousPeak = _peakSpeed;
        _peakSpeed = MathF.Max(speed, previousPeak * MathF.Exp(-dt / PeakReleaseSeconds));

        float holdRadius = StabilityRadius * 1.33f;
        if (_settled)
        {
            if (StabilityRadius > 0f && Vector2.Distance(inputPosition, _holdAnchor) <= holdRadius)
            {
                _previousInput = inputPosition;
                _peakSpeed = MathF.Min(_peakSpeed, FastAimThreshold);
                _output = _holdAnchor;
                return _output;
            }

            _settled = false;
            ResetStationaryCandidate();
        }

        if (UpdateStationaryCandidate(
            inputPosition,
            distance,
            speed,
            dt,
            previousPeak,
            holdRadius))
        {
            _settled = true;
            _holdAnchor = inputPosition;
            ResetStationaryCandidate();
            _previousInput = inputPosition;
            _output = inputPosition;
            return _output;
        }

        float speedDrop = 1f - speed / MathF.Max(previousPeak, 1f);
        float dropFactor = SmoothStep(speedDrop, 0.25f, 0.75f);
        float approachFactor = SmoothStep(
            previousPeak,
            FastAimThreshold * 0.50f,
            FastAimThreshold);
        float endpointFactor = 1f - SmoothStep(
            speed,
            FastAimThreshold * 0.29f,
            FastAimThreshold * 0.75f);
        float brakeAmount = Math.Clamp(
            StopAssist * dropFactor * approachFactor * endpointFactor,
            0f,
            MaximumBrakeAmount);

        // One two-tap FIR over the normal-stage positions. StabilityRadius does
        // not participate here, so it cannot duplicate Movement Anti-Chatter.
        _output = brakeAmount > 0f
            ? Vector2.Lerp(inputPosition, _previousInput, brakeAmount)
            : inputPosition;
        _output = LimitOffsetFromInput(_output, inputPosition, MaximumStopAssistOffset);
        if (!IsFinite(_output))
        {
            return Reset(inputPosition);
        }

        _previousInput = inputPosition;
        return _output;
    }

    private bool UpdateStationaryCandidate(
        Vector2 inputPosition,
        float reportDistance,
        float speed,
        float dt,
        float previousPeak,
        float holdRadius)
    {
        if (StabilityRadius <= 0f)
        {
            ResetStationaryCandidate();
            return false;
        }

        float speedDrop = 1f - speed / MathF.Max(previousPeak, 1f);
        bool strongDeceleration =
            _wasAboveStopCandidateSpeed &&
            previousPeak > MathF.Max(40f, FastAimThreshold * 0.60f) &&
            speed < StopCandidateSpeed &&
            speedDrop > 0.50f;

        if (speed >= StopCandidateSpeed)
        {
            _wasAboveStopCandidateSpeed = true;
        }
        else if (strongDeceleration || speed < StopCandidateSpeed * 0.80f)
        {
            _wasAboveStopCandidateSpeed = false;
        }

        if (strongDeceleration)
        {
            StartStationaryCandidate(inputPosition);
            return false;
        }

        if (speed > StopCandidateSpeed)
        {
            ResetStationaryCandidate();
            return false;
        }

        if (!_stationaryCandidate)
        {
            StartStationaryCandidate(inputPosition);
            return false;
        }

        _stationarySeconds += dt;
        _stationarySamples++;
        _stationaryPath += reportDistance;

        float netDistance = Vector2.Distance(inputPosition, _stationaryOrigin);
        float maximumSpread = MathF.Max(holdRadius * 2f, StabilityRadius * 1.5f);
        if (netDistance > maximumSpread)
        {
            StartStationaryCandidate(inputPosition);
            return false;
        }

        float coherence = _stationaryPath > 0.0005f
            ? netDistance / _stationaryPath
            : 0f;
        bool noProgress = _stationaryPath <= 0.0005f;
        bool reversingInPlace = coherence < StationaryCoherence;
        if (_stationarySeconds >= StopDwellSeconds &&
            _stationarySamples >= 2 &&
            (noProgress || reversingInPlace))
        {
            return true;
        }

        if (_stationarySeconds >= StopWindowSeconds)
        {
            StartStationaryCandidate(inputPosition);
        }

        return false;
    }

    private void StartStationaryCandidate(Vector2 inputPosition)
    {
        _stationaryCandidate = true;
        _stationarySamples = 0;
        _stationarySeconds = 0f;
        _stationaryPath = 0f;
        _stationaryOrigin = inputPosition;
    }

    private void ResetStationaryCandidate()
    {
        _stationaryCandidate = false;
        _stationarySamples = 0;
        _stationarySeconds = 0f;
        _stationaryPath = 0f;
        _stationaryOrigin = Vector2.Zero;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 LimitOffsetFromInput(
        Vector2 value,
        Vector2 inputPosition,
        float maximumOffset)
    {
        Vector2 offset = value - inputPosition;
        float lengthSquared = offset.LengthSquared();
        if (lengthSquared <= maximumOffset * maximumOffset)
        {
            return value;
        }

        float length = MathF.Sqrt(lengthSquared);
        return inputPosition + offset * (maximumOffset / length);
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
    private static float ClampFinite(float value, float minimum, float maximum, float fallback)
    {
        return float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
