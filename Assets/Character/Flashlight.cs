using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Put this on the Spot Light that is a child of the camera.
/// Call Toggle() from a UI button (OnClick) on mobile. F key toggles in the editor.
[RequireComponent(typeof(Light))]
public class Flashlight : MonoBehaviour
{
    public bool startOn = true;
    public float intensity = 3f;

    [Header("Flicker (optional)")]
    public bool flicker = true;
    [Range(0f, 1f)] public float flickerAmount = 0.15f;   // how much the light dips
    public float flickerSpeed = 12f;

    Light lamp;
    bool isOn;

    void Awake()
    {
        lamp = GetComponent<Light>();
        isOn = startOn;
        Apply(intensity);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) Toggle();
#endif
        if (!isOn) return;

        float level = intensity;
        if (flicker)
        {
            float n = Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
            level *= 1f - flickerAmount * n;
        }
        lamp.intensity = level;
    }

    public void Toggle()
    {
        isOn = !isOn;
        Apply(intensity);
    }

    void Apply(float level)
    {
        lamp.enabled = isOn;
        lamp.intensity = level;
    }
}
