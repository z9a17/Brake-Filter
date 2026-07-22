using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BrakeFilter.RadialDev;

/// <summary>
/// A bounded radial tracker whose fast path is based on physical distance per report.
/// It never predicts ahead of the raw pen position and does not buffer reports.
/// </summary>
internal sealed class LowLatencyRadialProcessor
{
    internal const float DefaultOuterRadius = 0.7039f;
    internal const float DefaultInnerRadius = 0.302f;
    internal const float DefaultSmoothing = 0.302f;
    internal const float DefaultSoftKnee = 0.603f;
    internal const float DefaultSmoothingLeak = 0.201f;
    internal const float DefaultFastRelease = 0.75f;

    internal const float MaximumRadius = 5f;
    internal const float MaximumSoftKnee = 10f;

    private const float MaximumJumpDistance = 1000f;
    private const float MaximumJumpDistanceSquared = MaximumJumpDistance * MaximumJumpDistance;
    private const float MinimumReleaseDistance = 0.02f;
    private const float ReleaseStartRadiusScale = 0.50f;
    private const float ReleaseEndRadiusScale = 1.50f;
    private const float LeakReleaseRetention = 0.50f;
    private const float DirectionEpsilonSquared = 1e-8f;

    private bool _initialized;
    private Vector2 _previousInput;
    private Vector2 _previousDelta;
    private Vector2 _output;

    private float _outerRadius = DefaultOuterRadius;
    private float _innerRadius = DefaultInnerRadius;
    private float _smoothing = DefaultSmoothing;
    private float _softKnee = DefaultSoftKnee;
    private float _smoothingLeak = DefaultSmoothingLeak;
    private float _fastRelease = DefaultFastRelease;

    public float OuterRadius
    {
        get => _outerRadius;
        set => _outerRadius = RadialMath.ClampFinite(value, 0f, MaximumRadius, DefaultOuterRadius);
    }

    public float InnerRadius
    {
        get => _innerRadius;
        set => _innerRadius = RadialMath.ClampFinite(value, 0f, MaximumRadius, DefaultInnerRadius);
    }

    public float Smoothing
    {
        get => _smoothing;
        set => _smoothing = RadialMath.ClampFinite(value, 0f, 1f, DefaultSmoothing);
    }

    public float SoftKnee
    {
        get => _softKnee;
        set => _softKnee = RadialMath.ClampFinite(value, 0f, MaximumSoftKnee, DefaultSoftKnee);
    }

    public float SmoothingLeak
    {
        get => _smoothingLeak;
        set => _smoothingLeak = RadialMath.ClampFinite(value, 0f, 1f, DefaultSmoothingLeak);
    }

    public float FastRelease
    {
        get => _fastRelease;
        set => _fastRelease = RadialMath.ClampFinite(value, 0f, 1f, DefaultFastRelease);
    }

    public Vector2 Output => _output;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 Process(Vector2 input)
    {
        if (!RadialMath.IsFinite(input))
        {
            Clear();
            return input;
        }

        if (!_initialized)
        {
            return Reset(input);
        }

        Vector2 rawDelta = input - _previousInput;
        float rawDistanceSquared = rawDelta.LengthSquared();
        if (!float.IsFinite(rawDistanceSquared) || rawDistanceSquared > MaximumJumpDistanceSquared)
        {
            return Reset(input);
        }

        float rawDistance = MathF.Sqrt(rawDistanceSquared);
        Vector2 error = input - _output;
        float errorSquared = error.LengthSquared();
        if (!float.IsFinite(errorSquared))
        {
            return Reset(input);
        }

        float outerRadius = MathF.Max(_outerRadius, 0f);
        float innerRadius = MathF.Min(_innerRadius, outerRadius);
        if (outerRadius <= 0f || errorSquared <= 0f)
        {
            _output = input;
            CommitInput(input, rawDelta);
            return _output;
        }

        // Preserve the configured chatter deadzone before considering release.
        // Direction changes that remain entirely inside it are still jitter.
        if (errorSquared <= innerRadius * innerRadius)
        {
            CommitInput(input, rawDelta);
            return _output;
        }

        float releaseStart = MathF.Max(MinimumReleaseDistance, innerRadius * ReleaseStartRadiusScale);
        float reversalRelease = CalculateReversalRelease(rawDelta, rawDistanceSquared, releaseStart);
        float motionRelease = CalculateMotionRelease(rawDistance, innerRadius, outerRadius);
        float requestedRelease = MathF.Max(reversalRelease, motionRelease * _fastRelease);

        // Leak retains a small amount of smoothing during fast release. A true
        // reversal still releases completely to avoid holding the old overshoot side.
        float effectiveRelease = reversalRelease >= 1f
            ? 1f
            : requestedRelease * (1f - _smoothingLeak * LeakReleaseRetention);

        float activeInnerRadius = innerRadius * (1f - effectiveRelease);
        float activeOuterRadius = RadialMath.Lerp(outerRadius, activeInnerRadius, effectiveRelease);
        if (errorSquared <= activeInnerRadius * activeInnerRadius)
        {
            CommitInput(input, rawDelta);
            return _output;
        }

        float errorDistance = MathF.Sqrt(errorSquared);
        float response = 1f - _smoothing;
        response = RadialMath.Lerp(response, 1f, effectiveRelease);

        float correctionDistance = (errorDistance - activeInnerRadius) * response;
        correctionDistance = MathF.Max(correctionDistance, errorDistance - activeOuterRadius);
        correctionDistance = Math.Clamp(correctionDistance, 0f, errorDistance);

        _output += error * (correctionDistance / errorDistance);
        if (!RadialMath.IsFinite(_output))
        {
            return Reset(input);
        }

        CommitInput(input, rawDelta);
        return _output;
    }

    public Vector2 Reset(Vector2 input)
    {
        if (!RadialMath.IsFinite(input))
        {
            Clear();
            return input;
        }

        _initialized = true;
        _previousInput = input;
        _previousDelta = Vector2.Zero;
        _output = input;
        return input;
    }

    public void Clear()
    {
        _initialized = false;
        _previousInput = Vector2.Zero;
        _previousDelta = Vector2.Zero;
        _output = Vector2.Zero;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CalculateMotionRelease(float rawDistance, float innerRadius, float outerRadius)
    {
        float releaseStart = MathF.Max(MinimumReleaseDistance, innerRadius * ReleaseStartRadiusScale);
        float releaseEnd = MathF.Max(releaseStart + MinimumReleaseDistance, outerRadius * ReleaseEndRadiusScale);
        float linear = Math.Clamp((rawDistance - releaseStart) / (releaseEnd - releaseStart), 0f, 1f);

        // Soft Knee blends from a compact smoothstep to a more gradual linear ramp.
        float softness = _softKnee / (1f + _softKnee);
        return RadialMath.Lerp(RadialMath.SmoothStep01(linear), linear, softness);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CalculateReversalRelease(
        Vector2 rawDelta,
        float rawDistanceSquared,
        float minimumDistance)
    {
        float previousDistanceSquared = _previousDelta.LengthSquared();
        float minimumDistanceSquared = minimumDistance * minimumDistance;
        if (rawDistanceSquared < minimumDistanceSquared ||
            previousDistanceSquared < minimumDistanceSquared)
        {
            return 0f;
        }

        float inverseLengths = 1f / MathF.Sqrt(rawDistanceSquared * previousDistanceSquared);
        float alignment = Math.Clamp(Vector2.Dot(rawDelta, _previousDelta) * inverseLengths, -1f, 1f);
        return alignment < 0f ? RadialMath.SmoothStep01(-alignment) : 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CommitInput(Vector2 input, Vector2 rawDelta)
    {
        _previousInput = input;
        if (rawDelta.LengthSquared() > DirectionEpsilonSquared)
        {
            _previousDelta = rawDelta;
        }
    }
}
