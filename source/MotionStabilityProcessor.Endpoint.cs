using System;
using System.Numerics;

namespace BrakeFilter;

public sealed partial class MotionStabilityProcessor
{
    private const float StopCandidateSpeed = 60f;
    private const float StopDwellSeconds = 0.010f;
    private const float StopWindowSeconds = 0.030f;
    private const float StationaryCoherence = 0.55f;
    private const float ApproachPeakThresholdRatio = 0.60f;
    private const float StopCandidateDisarmRatio = 0.80f;
    private const float CandidateSpreadScale = 2f;

    private bool _stationaryWindowActive;
    private bool _stopCandidateArmed;
    private int _stationaryWindowSamples;
    private float _stationaryWindowSeconds;
    private float _stationaryWindowPath;
    private Vector2 _stationaryWindowOrigin;

    /// <summary>
    /// Confirms a stop from a short low-speed window. Reports without a new
    /// coordinate may advance dwell time, but never arm or disarm velocity state.
    /// </summary>
    private bool UpdateStationaryWindow(
        Vector2 input,
        float reportDistance,
        float speed,
        float speedDrop,
        float reportPeriod,
        float previousPeak,
        float holdRadius,
        bool hasMotionSample)
    {
        if (StabilityRadius <= 0f)
        {
            ClearStationaryWindow();
            return false;
        }

        bool strongDeceleration =
            hasMotionSample &&
            _stopCandidateArmed &&
            previousPeak > MathF.Max(40f, MotionSpeedThreshold * ApproachPeakThresholdRatio) &&
            speed < StopCandidateSpeed &&
            speedDrop > 0.50f;

        if (hasMotionSample && speed >= StopCandidateSpeed)
        {
            _stopCandidateArmed = true;
        }
        else if (hasMotionSample &&
            (strongDeceleration || speed < StopCandidateSpeed * StopCandidateDisarmRatio))
        {
            _stopCandidateArmed = false;
        }

        if (strongDeceleration)
        {
            RestartStationaryWindow(input);
            return false;
        }

        if (hasMotionSample && speed >= StopCandidateSpeed)
        {
            ClearStationaryWindow();
            return false;
        }

        if (!_stationaryWindowActive)
        {
            RestartStationaryWindow(input);
            return false;
        }

        _stationaryWindowSeconds += reportPeriod;
        _stationaryWindowSamples++;
        _stationaryWindowPath += reportDistance;

        float netDistance = Vector2.Distance(input, _stationaryWindowOrigin);
        float maximumSpread = holdRadius * CandidateSpreadScale;
        if (netDistance > maximumSpread)
        {
            RestartStationaryWindow(input);
            return false;
        }

        float coherence = _stationaryWindowPath > 0.0005f
            ? netDistance / _stationaryWindowPath
            : 0f;
        bool stationary = _stationaryWindowPath <= 0.0005f || coherence < StationaryCoherence;
        if (_stationaryWindowSeconds >= StopDwellSeconds &&
            _stationaryWindowSamples >= 2 &&
            stationary)
        {
            return true;
        }

        if (_stationaryWindowSeconds >= StopWindowSeconds)
        {
            RestartStationaryWindow(input);
        }

        return false;
    }

    private void RestartStationaryWindow(Vector2 input)
    {
        _stationaryWindowActive = true;
        _stationaryWindowSamples = 0;
        _stationaryWindowSeconds = 0f;
        _stationaryWindowPath = 0f;
        _stationaryWindowOrigin = input;
    }

    private void ClearStationaryWindow()
    {
        _stationaryWindowActive = false;
        _stationaryWindowSamples = 0;
        _stationaryWindowSeconds = 0f;
        _stationaryWindowPath = 0f;
        _stationaryWindowOrigin = Vector2.Zero;
    }
}
