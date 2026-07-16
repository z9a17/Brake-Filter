using System;
using System.Runtime.CompilerServices;

namespace BrakeFilter;

/// <summary>
/// Estimates the tablet's stable report period without smoothing positions.
/// A window average absorbs host scheduling jitter and USB report batching.
/// </summary>
internal sealed class ReportPeriodEstimator
{
    // Index wrapping below relies on this remaining a power of two.
    private const int WindowSize = 32;
    private const int WarmupSamples = 4;
    private const float DefaultPeriodSeconds = 0.005f; // 200 Hz
    private const float MinimumPeriodSeconds = 0.00025f;
    private const float MaximumPeriodSeconds = 0.020f;

    private readonly float[] _samples = new float[WindowSize];
    private int _count;
    private int _next;
    private float _sum;

    public float PeriodSeconds => _count >= WarmupSamples
        ? _sum / _count
        : DefaultPeriodSeconds;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Observe(float measuredSeconds)
    {
        if (!float.IsFinite(measuredSeconds) ||
            measuredSeconds <= 0f ||
            measuredSeconds > MaximumPeriodSeconds)
        {
            return PeriodSeconds;
        }

        float sample = MathF.Max(measuredSeconds, MinimumPeriodSeconds);
        if (_count < WindowSize)
        {
            _samples[_next] = sample;
            _sum += sample;
            _count++;
        }
        else
        {
            _sum += sample - _samples[_next];
            _samples[_next] = sample;
        }

        _next = (_next + 1) & (WindowSize - 1);
        return PeriodSeconds;
    }

    public void Clear()
    {
        _count = 0;
        _next = 0;
        _sum = 0f;
    }
}
