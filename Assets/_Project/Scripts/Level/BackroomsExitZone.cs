using UnityEngine;

/// <summary>
/// Trigger zone and visual anchor for the Backrooms Level 0 escape threshold.
/// Placed procedurally by BackroomsRoomGenerator in a distant room.
/// When the player walks into the trigger, VictoryController.TriggerVictory() is fired.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BackroomsExitZone : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] string playerTag = "Player";

    [Header("Audio")]
    [Tooltip("Ambient beacon hum that helps the player locate the exit when nearby.")]
    [SerializeField] AudioClip beaconHumAudio;
    [Range(0f, 1f)]
    [SerializeField] float beaconHumVolume = 0.5f;
    [SerializeField] float maxHearingDistance = 18f;

    BoxCollider triggerCollider;
    AudioSource audioSource;
    bool triggered;

    void Awake()
    {
        triggerCollider = GetComponent<BoxCollider>();
        triggerCollider.isTrigger = true;

        SetupBeaconAudio();
    }

    void SetupBeaconAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 1f; // Full 3D
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 2f;
        audioSource.maxDistance = maxHearingDistance;
        audioSource.volume = beaconHumVolume;

        if (beaconHumAudio == null)
        {
            // Use subtle ambient tone
            beaconHumAudio = Resources.Load<AudioClip>("SFX/juanjo_ sound - Pseudoliminality");
        }

        if (beaconHumAudio != null)
        {
            audioSource.clip = beaconHumAudio;
            audioSource.Play();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        TryTriggerVictory(other);
    }

    void OnTriggerStay(Collider other)
    {
        TryTriggerVictory(other);
    }

    void TryTriggerVictory(Collider other)
    {
        if (triggered) return;

        Transform playerRoot = null;
        if (other.CompareTag(playerTag))
        {
            playerRoot = other.transform;
        }
        else
        {
            CharacterController cc = other.GetComponent<CharacterController>() ?? other.GetComponentInParent<CharacterController>();
            if (cc != null)
            {
                playerRoot = cc.transform;
            }
            else
            {
                var tpc = other.GetComponentInParent<StarterAssets.ThirdPersonController>();
                if (tpc != null) playerRoot = tpc.transform;
            }
        }

        if (playerRoot == null) return;

        // 1. Position check in doorway local space:
        // Ensures player is actually walking through the front of the doorway opening,
        // never from behind the wall or merely brushing by an adjacent hallway.
        Vector3 localPos = transform.InverseTransformPoint(playerRoot.position);

        // Width opening between left and right posts (posts at +-0.85m)
        if (Mathf.Abs(localPos.x) > 0.85f) return;
        // Height check (doorway opening is 2.45m high)
        if (localPos.y < -0.3f || localPos.y > 2.5f) return;
        // Depth check: entering from room front (-Z) through threshold (+0.1m).
        // Must NOT be behind the wall (localPos.z > 0.25m).
        if (localPos.z > 0.25f || localPos.z < -0.85f) return;

        // 2. Line of sight check: verify no solid wall separates the doorway center from the player.
        // This guarantees that approaching the door from behind a partition wall will never trigger escape.
        Vector3 doorwayThresholdWorld = transform.TransformPoint(new Vector3(0f, 1.2f, -0.15f));
        Vector3 playerCenterWorld = playerRoot.position + Vector3.up * 1.0f;
        Vector3 dir = playerCenterWorld - doorwayThresholdWorld;
        float dist = dir.magnitude;

        if (dist > 0.05f)
        {
            if (Physics.Raycast(doorwayThresholdWorld, dir.normalized, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                bool hitIsPlayer = hit.transform == playerRoot ||
                                   hit.transform.IsChildOf(playerRoot) ||
                                   playerRoot.IsChildOf(hit.transform);
                if (!hitIsPlayer)
                {
                    // Hit a wall or obstacle between the door and player; ignore trigger
                    return;
                }
            }
        }

        triggered = true;
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        VictoryController.TriggerVictory();
    }

    /// <summary>
    /// Procedurally builds an exit doorway with frame, illuminated neon sign, and green light.
    /// </summary>
    public static GameObject BuildExitPortal(Vector3 position, Quaternion rotation, Material wallMat, Material lightMat)
    {
        GameObject exitObj = new GameObject("BackroomsExit");
        exitObj.transform.position = position;
        exitObj.transform.rotation = rotation;

        // Trigger Collider: tightly scoped to the doorway opening so it never extends through back walls
        BoxCollider box = exitObj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1.5f, 2.3f, 0.7f);
        box.center = new Vector3(0f, 1.15f, -0.15f);

        exitObj.AddComponent<BackroomsExitZone>();

        // Door frame - Left post
        CreateBoxPart(exitObj.transform, "Exit_Frame_Left", new Vector3(-0.85f, 1.25f, 0f), new Vector3(0.18f, 2.5f, 0.28f), wallMat);
        // Door frame - Right post
        CreateBoxPart(exitObj.transform, "Exit_Frame_Right", new Vector3(0.85f, 1.25f, 0f), new Vector3(0.18f, 2.5f, 0.28f), wallMat);
        // Door frame - Top lintel
        CreateBoxPart(exitObj.transform, "Exit_Frame_Top", new Vector3(0f, 2.45f, 0f), new Vector3(1.88f, 0.18f, 0.28f), wallMat);

        // Dark threshold / portal void behind the door frame
        GameObject voidPlane = CreateBoxPart(exitObj.transform, "Exit_Threshold_Void", new Vector3(0f, 1.2f, 0.1f), new Vector3(1.55f, 2.3f, 0.08f), null);
        var voidRend = voidPlane.GetComponent<Renderer>();
        if (voidRend != null)
        {
            Material voidMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            voidMat.color = new Color(0.02f, 0.04f, 0.02f);
            voidRend.sharedMaterial = voidMat;
        }

        // EXIT Sign Housing
        GameObject signBox = CreateBoxPart(exitObj.transform, "Exit_Sign_Housing", new Vector3(0f, 2.7f, 0f), new Vector3(0.85f, 0.32f, 0.16f), null);
        var signRend = signBox.GetComponent<Renderer>();
        if (signRend != null)
        {
            Material signMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            signMat.color = new Color(0.15f, 0.65f, 0.35f);
            signMat.EnableKeyword("_EMISSION");
            signMat.SetColor("_EmissionColor", new Color(0.15f, 1.4f, 0.45f) * 2.2f);
            signRend.sharedMaterial = signMat;
        }

        // Green Emergency Light
        GameObject lightObj = new GameObject("Exit_Light");
        lightObj.transform.SetParent(exitObj.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 2.6f, -0.35f);

        Light lightComp = lightObj.AddComponent<Light>();
        lightComp.type = LightType.Point;
        lightComp.color = new Color(0.25f, 0.98f, 0.45f);
        lightComp.intensity = 2.8f;
        lightComp.range = 8.5f;
        lightComp.shadows = LightShadows.None;

        return exitObj;
    }

    static GameObject CreateBoxPart(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;

        // Visual frame parts don't need physical collision
        Collider c = part.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);

        if (mat != null)
        {
            var rend = part.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = mat;
        }
        return part;
    }
}
