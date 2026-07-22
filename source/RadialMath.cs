using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BrakeFilter.RadialDev;

internal static class RadialMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ClampFinite(float value, float minimum, float maximum, float fallback)
    {
        return float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep01(float value)
    {
        float amount = Math.Clamp(value, 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float start, float end, float amount)
    {
        return start + (end - start) * amount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SafeScale(float millimetres, float maximumRaw)
    {
        float scale = millimetres / maximumRaw;
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
