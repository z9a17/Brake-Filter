using System;
using System.Numerics;

namespace BrakeFilter;

public sealed partial class AdvancedAimEngine
{
    private const float StopCandidateSpeed = 60f;
    private const float StopDwellSeconds = 0.010f;
    private const float StopWindowSeconds = 0.030f;
    private const float StationaryCoherence = 0.55f;

    private bool _stationaryCandidate;
    private bool _wasAboveStopCandidateSpeed;
    private int _stationarySamples;
    private float _stationarySeconds;
    private float _stationaryPath;
    private Vector2 _stationaryOrigin;

    private bool UpdateStationaryCandidate(
        Vector2 input,
        float reportDistance,
        float speed,
        float reportPeriod,
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
            StartStationaryCandidate(input);
            return false;
        }

        if (speed > StopCandidateSpeed)
        {
            ResetStationaryCandidate();
            return false;
        }

        if (!_stationaryCandidate)
        {
            StartStationaryCandidate(input);
            return false;
        }

        _stationarySeconds += reportPeriod;
        _stationarySamples++;
        _stationaryPath += reportDistance;

        float netDistance = Vector2.Distance(input, _stationaryOrigin);
        float maximumSpread = MathF.Max(holdRadius * 2f, StabilityRadius * 1.5f);
        if (netDistance > maximumSpread)
        {
            StartStationaryCandidate(input);
            return false;
        }

        float coherence = _stationaryPath > 0.0005f
            ? netDistance / _stationaryPath
            : 0f;
        bool stationary = _stationaryPath <= 0.0005f || coherence < StationaryCoherence;
        if (_stationarySeconds >= StopDwellSeconds && _stationarySamples >= 2 && stationary)
        {
            return true;
        }

        if (_stationarySeconds >= StopWindowSeconds)
        {
            StartStationaryCandidate(input);
        }

        return false;
    }

    private void StartStationaryCandidate(Vector2 input)
    {
        _stationaryCandidate = true;
        _stationarySamples = 0;
        _stationarySeconds = 0f;
        _stationaryPath = 0f;
        _stationaryOrigin = input;
    }

    private void ResetStationaryCandidate()
    {
        _stationaryCandidate = false;
        _stationarySamples = 0;
        _stationarySeconds = 0f;
        _stationaryPath = 0f;
        _stationaryOrigin = Vector2.Zero;
    }
}
