using UnityEngine;

/// <summary>
/// Makes a ceiling light stutter like a dying fluorescent tube. Attached to a
/// few of the level's panel lights by <see cref="BackroomsRoomGenerator"/>; the
/// light stays at full brightness for a while, then dims for a few frames.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlickeringLight : MonoBehaviour
{
    [Tooltip("Seconds the light stays steady between glitches.")]
    public Vector2 steadyTime = new Vector2(2f, 8f);

    [Tooltip("Seconds the light stays dim during a glitch.")]
    public Vector2 flickerTime = new Vector2(0.03f, 0.12f);

    [Tooltip("How much of its brightness the light keeps while glitching.")]
    [Range(0f, 1f)] public float dimTo = 0.15f;

    Light source;
    float baseIntensity;
    float timer;
    bool dim;

    void Awake()
    {
        source = GetComponent<Light>();
        baseIntensity = source.intensity;
        timer = Random.Range(steadyTime.x, steadyTime.y);
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        dim = !dim;

        if (dim)
        {
            source.intensity = baseIntensity * dimTo;
            timer = Random.Range(flickerTime.x, flickerTime.y);
        }
        else
        {
            source.intensity = baseIntensity;
            timer = Random.Range(steadyTime.x, steadyTime.y);
        }
    }
}
