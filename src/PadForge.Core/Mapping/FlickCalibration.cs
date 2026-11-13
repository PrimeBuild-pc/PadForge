namespace PadForge.Core.Mapping;

/// <summary>
/// Turns the fastest mouse speed seen during a flick into the sensitivity
/// that reaches a full stick at that speed.
/// </summary>
public static class FlickCalibration
{
    public const float MinimumCountsPerSecond = 640f;

    public static bool TrySensitivity(float countsPerSecond, out float sensitivity)
    {
        if (countsPerSecond < MinimumCountsPerSecond || float.IsNaN(countsPerSecond))
        {
            sensitivity = 0f;
            return false;
        }

        sensitivity = StickTuning.Clamp(
            countsPerSecond / MouseToStickConverter.FullScaleCountsPerSecond,
            curve: 1.4,
            deadzone: 0.06,
            spring: true,
            springRate: 26).Sensitivity;
        return true;
    }
}
