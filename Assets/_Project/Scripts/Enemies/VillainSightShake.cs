using System;
using UnityEngine;
using Unity.Cinemachine;   // Cinemachine 3 (Unity 6). For Cinemachine 2 see the notes in the chat.

/// Put this on the villain. It checks whether the villain can see the player
/// (range + view cone + nothing blocking the line) and shakes the camera while it can.
public class VillainSightShake : MonoBehaviour
{
    [Header("References")]
    public Transform player;                              // PlayerCapsule
    public CinemachineBasicMultiChannelPerlin noise;      // on PlayerFollowCamera

    [Header("Sight")]
    public float sightRange = 18f;
    [Range(10f, 360f)] public float viewAngle = 120f;     // total cone angle
    public float eyeHeight = 1.5f;                        // villain eye above its pivot
    public float playerAimHeight = 1.0f;                  // point on the player to look at

    [Header("Shake")]
    public float maxAmplitude = 1.5f;
    public float frequency = 2f;
    public float fadeIn = 6f;                             // how fast the shake ramps up
    public float fadeOut = 2f;                            // how fast it calms down
    public bool scaleWithDistance = true;                 // closer villain = stronger shake

    float current;

    void Update()
    {
        if (player == null || noise == null) return;

        float target = 0f;
        if (CanSeePlayer(out float dist))
        {
            // Cap shake amplitude to 0.85 so the camera remains clear and playable
            target = Mathf.Min(maxAmplitude, 0.85f);
            if (scaleWithDistance)
                target *= Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(dist / sightRange));
        }

        float speed = target > current ? fadeIn : fadeOut;
        current = Mathf.MoveTowards(current, target, speed * Time.deltaTime);

        noise.AmplitudeGain = current;
        noise.FrequencyGain = frequency;
    }

    bool CanSeePlayer(out float dist)
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 aim = player.position + Vector3.up * playerAimHeight;
        Vector3 toPlayer = aim - eye;
        dist = toPlayer.magnitude;

        if (dist > sightRange) return false;

        Vector3 flat = new Vector3(toPlayer.x, 0f, toPlayer.z);
        if (Vector3.Angle(transform.forward, flat) > viewAngle * 0.5f) return false;

        // Nearest thing along the line that is not the villain itself
        RaycastHit[] hits = Physics.RaycastAll(eye, toPlayer.normalized, dist,
            ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit h in hits)
        {
            if (h.transform.IsChildOf(transform)) continue;       // skip own colliders
            return h.transform == player || h.transform.IsChildOf(player);
        }
        return false;
    }
}
