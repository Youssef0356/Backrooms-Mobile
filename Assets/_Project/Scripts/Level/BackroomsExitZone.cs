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
        if (triggered) return;

        bool isPlayer = other.CompareTag(playerTag) ||
                        other.GetComponent<CharacterController>() != null ||
                        other.GetComponentInParent<StarterAssets.ThirdPersonController>() != null;

        if (isPlayer)
        {
            triggered = true;
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            VictoryController.TriggerVictory();
        }
    }

    /// <summary>
    /// Procedurally builds an exit doorway with frame, illuminated neon sign, and green light.
    /// </summary>
    public static GameObject BuildExitPortal(Vector3 position, Quaternion rotation, Material wallMat, Material lightMat)
    {
        GameObject exitObj = new GameObject("BackroomsExit");
        exitObj.transform.position = position;
        exitObj.transform.rotation = rotation;

        // Trigger Collider
        BoxCollider box = exitObj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(2.4f, 2.8f, 2.4f);
        box.center = new Vector3(0f, 1.4f, 0f);

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
