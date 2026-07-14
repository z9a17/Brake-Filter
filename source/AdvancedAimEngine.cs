using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BrakeFilter;

/// <summary>
/// Allocation-free spatial aim filter. All positions are physical millimetres
/// and time is expressed in seconds.
/// </summary>
public sealed class AdvancedAimEngine
{
    public const float DefaultStabilityRadius = 0.05f;
    public const float DefaultStopAssist = 0.25f;
    public const float DefaultFastAimStability = 0.80f;
    public const float DefaultFastAimThreshold = 120f;

    public const float MaximumStabilityRadius = 0.20f;
    public const float MaximumStopAssist = 0.50f;
    public const float MaximumFastAimStability = 1f;
    public const float MinimumFastAimThreshold = 40f;
    public const float MaximumFastAimThreshold = 500f;

    private const float MinimumDeltaTime = 0.00025f;
    private const float MaximumDeltaTime = 0.020f;
    private const float ResetTime = 0.050f;
    private const float PeakReleaseSeconds = 0.050f;
    private const float AxisBlendSeconds = 0.012f;
    private const float CornerCosine = 0.50f;
    private const float StopCandidateSpeed = 60f;
    private const float StopDwellSeconds = 0.010f;
    private const float StopWindowSeconds = 0.030f;
    private const float StationaryCoherence = 0.55f;
    private const float MaximumBrakeAmount = 0.85f;
    private const float Tiny = 1e-12f;

    private bool _initialized;
    private bool _settled;
    private bool _stationaryCandidate;
    private bool _wasAboveStopCandidateSpeed;
    private int _stationarySamples;
    private float _stationarySeconds;
    private float _stationaryPath;
    private Vector2 _stationaryOrigin;
    private Vector2 _previousRaw;
    private Vector2 _target;
    private Vector2 _output;
    private Vector2 _axis;
    private float _peakSpeed;

    private float _stabilityRadius = DefaultStabilityRadius;
    private float _stopAssist = DefaultStopAssist;
    private float _fastAimStability = DefaultFastAimStability;
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

    public float FastAimStability
    {
        get => _fastAimStability;
        set => _fastAimStability = ClampFinite(value, 0f, MaximumFastAimStability, DefaultFastAimStability);
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
        _previousRaw = Vector2.Zero;
        _target = Vector2.Zero;
        _output = Vector2.Zero;
        _axis = Vector2.Zero;
        _peakSpeed = 0f;
    }

    public Vector2 Reset(Vector2 rawPosition)
    {
        if (!IsFinite(rawPosition))
        {
            Clear();
            return rawPosition;
        }

        _initialized = true;
        _settled = false;
        _wasAboveStopCandidateSpeed = false;
        ResetStationaryCandidate();
        _previousRaw = rawPosition;
        _target = rawPosition;
        _output = rawPosition;
        _axis = Vector2.Zero;
        _peakSpeed = 0f;
        return rawPosition;
    }

    public Vector2 Process(Vector2 rawPosition, float deltaTimeSeconds)
    {
        if (!IsFinite(rawPosition))
        {
            Clear();
            return rawPosition;
        }

        if (!_initialized || !float.IsFinite(deltaTimeSeconds) || deltaTimeSeconds <= 0f || deltaTimeSeconds > ResetTime)
        {
            return Reset(rawPosition);
        }

        float dt = Math.Clamp(deltaTimeSeconds, MinimumDeltaTime, MaximumDeltaTime);
        Vector2 rawDelta = rawPosition - _previousRaw;
        float distanceSquared = rawDelta.LengthSquared();
        if (!float.IsFinite(distanceSquared))
        {
            return Reset(rawPosition);
        }

        float distance = MathF.Sqrt(distanceSquared);
        float speed = distance / dt;
        float previousPeak = _peakSpeed;
        _peakSpeed = MathF.Max(speed, previousPeak * MathF.Exp(-dt / PeakReleaseSeconds));

        float holdRadius = StabilityRadius * 1.33f;
        float maximumOffset = MathF.Max(0.080f, StabilityRadius * 2f);

        if (_settled)
        {
            float anchorDistance = Vector2.Distance(rawPosition, _target);
            if (StabilityRadius > 0f && anchorDistance <= holdRadius)
            {
                _previousRaw = rawPosition;
                _peakSpeed = MathF.Min(_peakSpeed, FastAimThreshold);
                _output = _target;
                return _output;
            }

            _settled = false;
            ResetStationaryCandidate();
            _axis = distance > Tiny ? rawDelta / distance : Vector2.Zero;
        }

        bool cornerReset = UpdateUnsignedAxis(rawDelta, distance, speed, dt);
        Vector2 previousTarget = _target;

        if (cornerReset)
        {
            // A fast 60-90 degree turn is intentional much more often than it is
            // chatter. Passing this one report prevents corner cutting.
            _target = rawPosition;
        }
        else
        {
            float fastBlend = SmoothStep(speed, FastAimThreshold * 0.21f, FastAimThreshold);
            ApplySpatialLeash(rawPosition, fastBlend);
            _target = LimitOffsetFromRaw(_target, rawPosition, maximumOffset);
        }

        if (UpdateStationaryCandidate(
            rawPosition,
            distance,
            speed,
            dt,
            previousPeak,
            holdRadius))
        {
            // Anchor to the already anti-chattered target. This ends the FIR in
            // one step and guarantees that the cursor cannot creep after a stop.
            _settled = true;
            ResetStationaryCandidate();
            _previousRaw = rawPosition;
            _output = _target;
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
        float decelerationFactor = dropFactor * approachFactor * endpointFactor;
        float brakeAmount = Math.Clamp(
            StopAssist * decelerationFactor,
            0f,
            MaximumBrakeAmount);

        // This is a two-tap FIR over anti-chattered targets, not a recursive EMA.
        // Therefore any braking tail is strictly bounded to one input report.
        _output = Vector2.Lerp(_target, previousTarget, brakeAmount);
        _output = LimitOffsetFromRaw(_output, rawPosition, maximumOffset);
        if (!IsFinite(_output))
        {
            return Reset(rawPosition);
        }

        _previousRaw = rawPosition;
        return _output;
    }

    private bool UpdateUnsignedAxis(Vector2 rawDelta, float distance, float speed, float dt)
    {
        if (distance <= Tiny || speed < FastAimThreshold * 0.21f)
        {
            return false;
        }

        Vector2 currentDirection = rawDelta / distance;
        if (_axis.LengthSquared() <= Tiny)
        {
            _axis = currentDirection;
            return false;
        }

        float signedDot = Vector2.Dot(_axis, currentDirection);
        float unsignedDot = MathF.Abs(signedDot);
        float cornerSpeed = MathF.Max(40f, FastAimThreshold * 0.67f);
        if (speed >= cornerSpeed && unsignedDot < CornerCosine)
        {
            _axis = currentDirection;
            return true;
        }

        // Treat the motion axis as unsigned. A 180-degree reverse jump stays on
        // the same stable line instead of cancelling the direction vector.
        if (signedDot < 0f)
        {
            currentDirection = -currentDirection;
        }

        float amount = 1f - MathF.Exp(-dt / AxisBlendSeconds);
        Vector2 blended = Vector2.Lerp(_axis, currentDirection, amount);
        float lengthSquared = blended.LengthSquared();
        _axis = lengthSquared > Tiny && float.IsFinite(lengthSquared)
            ? blended / MathF.Sqrt(lengthSquared)
            : currentDirection;
        return false;
    }

    private bool UpdateStationaryCandidate(
        Vector2 rawPosition,
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

        // Begin a fresh endpoint window at the first large speed drop. This
        // prevents the preceding fast stroke from making a true stop appear
        // directionally coherent.
        if (strongDeceleration)
        {
            StartStationaryCandidate(rawPosition);
            return false;
        }

        float localDistance = Vector2.Distance(rawPosition, _target);
        bool eligible = speed <= StopCandidateSpeed || localDistance <= holdRadius * 1.25f;
        if (!eligible)
        {
            ResetStationaryCandidate();
            return false;
        }

        if (!_stationaryCandidate)
        {
            StartStationaryCandidate(rawPosition);
            return false;
        }

        _stationarySeconds += dt;
        _stationarySamples++;
        _stationaryPath += reportDistance;

        float netDistance = Vector2.Distance(rawPosition, _stationaryOrigin);
        float maximumSpread = MathF.Max(holdRadius * 2f, StabilityRadius * 1.5f);
        if (netDistance > maximumSpread)
        {
            StartStationaryCandidate(rawPosition);
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
            StartStationaryCandidate(rawPosition);
        }

        return false;
    }

    private void StartStationaryCandidate(Vector2 rawPosition)
    {
        _stationaryCandidate = true;
        _stationarySamples = 0;
        _stationarySeconds = 0f;
        _stationaryPath = 0f;
        _stationaryOrigin = rawPosition;
    }

    private void ResetStationaryCandidate()
    {
        _stationaryCandidate = false;
        _stationarySamples = 0;
        _stationarySeconds = 0f;
        _stationaryPath = 0f;
        _stationaryOrigin = Vector2.Zero;
    }

    private void ApplySpatialLeash(Vector2 rawPosition, float fastBlend)
    {
        Vector2 error = rawPosition - _target;
        if (StabilityRadius <= 0f)
        {
            _target = rawPosition;
            return;
        }

        if (_axis.LengthSquared() <= Tiny || fastBlend <= 0f)
        {
            _target += Shrink(error, StabilityRadius);
            return;
        }

        float fastParallelRadius = MathF.Max(0.004f, StabilityRadius * 0.18f);
        float fastPerpendicularRadius = Lerp(
            StabilityRadius * 0.35f,
            StabilityRadius * 1.50f,
            FastAimStability);
        float parallelRadius = Lerp(StabilityRadius, fastParallelRadius, fastBlend);
        float perpendicularRadius = Lerp(StabilityRadius, fastPerpendicularRadius, fastBlend);

        float parallelDistance = Vector2.Dot(error, _axis);
        Vector2 perpendicular = error - _axis * parallelDistance;
        _target += _axis * ShrinkScalar(parallelDistance, parallelRadius) +
            Shrink(perpendicular, perpendicularRadius);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 Shrink(Vector2 value, float radius)
    {
        if (radius <= 0f)
        {
            return value;
        }

        float lengthSquared = value.LengthSquared();
        if (lengthSquared <= radius * radius)
        {
            return Vector2.Zero;
        }

        float length = MathF.Sqrt(lengthSquared);
        return value * ((length - radius) / length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ShrinkScalar(float value, float radius)
    {
        float magnitude = MathF.Abs(value);
        return magnitude <= radius ? 0f : MathF.CopySign(magnitude - radius, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 LimitOffsetFromRaw(Vector2 value, Vector2 rawPosition, float maximumOffset)
    {
        Vector2 offset = value - rawPosition;
        float lengthSquared = offset.LengthSquared();
        if (lengthSquared <= maximumOffset * maximumOffset)
        {
            return value;
        }

        float length = MathF.Sqrt(lengthSquared);
        return rawPosition + offset * (maximumOffset / length);
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
