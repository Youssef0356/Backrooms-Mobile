using UnityEngine;

/// <summary>
/// Centralized platform detection utility.
/// Reliably differentiates mobile (Android/iOS) from desktop (Windows/Mac/Linux standalone),
/// preventing false positives from PC touchscreen monitors, tablets, or virtual touch drivers (Input.touchSupported).
/// </summary>
public static class PlatformUtils
{
    /// <summary>
    /// Returns true only when running on a genuine mobile device (Android/iOS),
    /// or when running under a mobile runtime. Standalone desktop builds and PC editor always return false.
    /// </summary>
    public static bool IsMobile
    {
        get
        {
#if UNITY_ANDROID || UNITY_IOS
            return !Application.isEditor;
#elif UNITY_STANDALONE || UNITY_EDITOR
            return false;
#else
            return Application.isMobilePlatform;
#endif
        }
    }

    /// <summary>
    /// Returns true when running on a Desktop / PC environment (Windows, macOS, Linux).
    /// </summary>
    public static bool IsDesktop => !IsMobile;
}
