using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BrakeFilter;

internal readonly struct MotionFrame
{
    public MotionFrame(float elapsedPeriod, float speed, bool hasMotionSample)
    {
        ElapsedPeriod = elapsedPeriod;
        Speed = speed;
        HasMotionSample = hasMotionSample;
    }

    public float ElapsedPeriod { get; }

    /// <summary>Physical speed in mm/s; valid only when HasMotionSample is true.</summary>
    public float Speed { get; }

    /// <summary>Whether this report contains a trustworthy changed-coordinate sample.</summary>
    public bool HasMotionSample { get; }
}

/// <summary>
/// Separates transport reports from actual coordinate samples. Duplicate X/Y
/// reports still advance elapsed time but never become zero-velocity samples.
/// </summary>
internal sealed class PositionMotionEstimator
{
    private const float MaximumPositionInterval = 0.020f;

    private readonly ReportPeriodEstimator _arrivalPeriod = new();
    private readonly ReportPeriodEstimator _positionPeriod = new();
    private Vector2 _lastPosition;
    private float _elapsedSincePosition;
    private bool _initialized;

    public void Reset(Vector2 position)
    {
        _arrivalPeriod.Clear();
        _positionPeriod.Clear();
        _lastPosition = position;
        _elapsedSincePosition = 0f;
        _initialized = MotionMath.IsFinite(position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotionFrame Observe(Vector2 position, float measuredArrivalPeriod)
    {
        if (!MotionMath.IsFinite(position) ||
            !float.IsFinite(measuredArrivalPeriod) ||
            measuredArrivalPeriod <= 0f)
        {
            return new MotionFrame(measuredArrivalPeriod, 0f, false);
        }

        float elapsedPeriod = _arrivalPeriod.Observe(measuredArrivalPeriod);
        if (!_initialized)
        {
            Reset(position);
            return new MotionFrame(elapsedPeriod, 0f, false);
        }

        _elapsedSincePosition = MathF.Min(
            _elapsedSincePosition + measuredArrivalPeriod,
            1f);

        if (position == _lastPosition)
        {
            return new MotionFrame(elapsedPeriod, 0f, false);
        }

        Vector2 delta = position - _lastPosition;
        float distanceSquared = delta.LengthSquared();
        _lastPosition = position;

        float observedPositionPeriod = _elapsedSincePosition;
        _elapsedSincePosition = 0f;
        if (observedPositionPeriod > MaximumPositionInterval)
        {
            // The device may have moved at any point during a long interval,
            // so neither the accumulated time nor the normal sample period is
            // a trustworthy divisor. Emit the report without using this one
            // coordinate change for velocity-based features.
            return new MotionFrame(elapsedPeriod, 0f, false);
        }

        float positionPeriod = _positionPeriod.Observe(observedPositionPeriod);
        float speed = float.IsFinite(distanceSquared)
            ? MathF.Sqrt(distanceSquared) / positionPeriod
            : 0f;
        return new MotionFrame(elapsedPeriod, speed, true);
    }

    public void Clear()
    {
        _arrivalPeriod.Clear();
        _positionPeriod.Clear();
        _lastPosition = Vector2.Zero;
        _elapsedSincePosition = 0f;
        _initialized = false;
    }
}
