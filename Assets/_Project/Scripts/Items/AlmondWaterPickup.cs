using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

/// <summary>
/// Collectible Almond Water bottle placed in the Backrooms (on tables, desks, or corners).
/// Interacting with it adds it to the player's inventory to reduce hallucinations and restore stamina.
/// </summary>
[DisallowMultipleComponent]
public class AlmondWaterPickup : MonoBehaviour
{
    public static readonly List<AlmondWaterPickup> ActivePickups = new List<AlmondWaterPickup>();

    [Header("Pickup Settings")]
    [Tooltip("Maximum interaction distance in metres.")]
    public float interactDistance = 2.4f;

    [Tooltip("Subtle vertical floating bob speed.")]
    public float bobSpeed = 2.2f;
    [Tooltip("Vertical floating bob height amplitude in metres.")]
    public float bobHeight = 0.035f;

    [Tooltip("Slow rotational spin speed (deg/sec).")]
    public float spinSpeed = 35.0f;

    private Transform _playerTransform;
    private Camera _playerCamera;
    private Vector3 _basePosition;
    private float _bobTimer;
    private bool _isCollected;

    private static AudioClip _pickupSound;

    private void Awake()
    {
        _basePosition = transform.position;
        _bobTimer = Random.Range(0f, Mathf.PI * 2f);

        SetupVisuals();
    }

    private void OnEnable()
    {
        ActivePickups.Add(this);
    }

    private void OnDisable()
    {
        ActivePickups.Remove(this);
    }

    private void Start()
    {
        FindPlayer();
    }

    private void FindPlayer()
    {
        FirstPersonController fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null)
        {
            _playerTransform = fpc.transform;
            _playerCamera = fpc.GetComponentInChildren<Camera>();
        }
        if (_playerCamera == null)
        {
            _playerCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (_isCollected) return;

        // Visual hover / float animation
        _bobTimer += Time.deltaTime * bobSpeed;
        float yOffset = Mathf.Sin(_bobTimer) * bobHeight;
        transform.position = _basePosition + Vector3.up * yOffset;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    private void SetupVisuals()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = AlmondWaterMeshBuilder.GetOrCreateBottleMesh();

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new Material[]
        {
            AlmondWaterMeshBuilder.GetOrCreateBottleMaterial(),
            AlmondWaterMeshBuilder.GetOrCreateLabelMaterial()
        };

        // Add soft warm phosphor glint light so it stands out in dim backrooms
        GameObject glintObj = new GameObject("GlintLight");
        glintObj.transform.SetParent(transform, false);
        glintObj.transform.localPosition = new Vector3(0f, 0.12f, 0f);

        Light glint = glintObj.AddComponent<Light>();
        glint.type = LightType.Point;
        glint.color = new Color(1.0f, 0.88f, 0.60f); // warm almond amber
        glint.range = 2.0f;
        glint.intensity = 0.85f;

        // Ensure collider for interaction raycasting
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 0.28f;
            capsule.radius = 0.10f;
            capsule.center = new Vector3(0f, 0.13f, 0f);
            capsule.isTrigger = true;
        }
    }

    /// <summary>
    /// Checks whether the player is close enough and looking at this bottle.
    /// </summary>
    public bool CanPlayerInteract(Vector3 eyePos, Vector3 lookDir, out float distance)
    {
        distance = Vector3.Distance(eyePos, transform.position + Vector3.up * 0.12f);
        if (distance > interactDistance) return false;

        Vector3 toBottle = ((transform.position + Vector3.up * 0.12f) - eyePos).normalized;
        float dot = Vector3.Dot(lookDir, toBottle);
        return dot > 0.65f; // within field of view
    }

    /// <summary>
    /// Collects this Almond Water bottle into the player inventory.
    /// </summary>
    public void Collect()
    {
        if (_isCollected) return;

        PlayerInventory inv = PlayerInventory.Instance;
        if (inv != null)
        {
            if (!inv.AddAlmondWater())
            {
                // Inventory full
                return;
            }
        }

        _isCollected = true;
        PlayPickupSound(transform.position);

        Destroy(gameObject);
    }

    private static void PlayPickupSound(Vector3 position)
    {
        if (_pickupSound == null)
        {
            _pickupSound = CreatePickupAudioClip();
        }

        GameObject audioObj = new GameObject("AlmondWater_PickupSFX");
        audioObj.transform.position = position;
        AudioSource src = audioObj.AddComponent<AudioSource>();
        src.clip = _pickupSound;
        src.volume = 0.95f;
        src.spatialBlend = 0.4f;
        src.Play();
        Destroy(audioObj, 1.5f);
    }

    private static AudioClip CreatePickupAudioClip()
    {
        // High quality glass bottle clink + soft fluid slosh
        int sampleRate = 44100;
        int sampleCount = (int)(sampleRate * 0.45f);
        AudioClip clip = AudioClip.Create("AlmondWaterPickup", sampleCount, 1, sampleRate, false);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;

            // Glass clink: high harmonics decaying rapidly
            float f1 = 2800f;
            float f2 = 4200f;
            float f3 = 5600f;
            float env = Mathf.Exp(-t * 22f);

            float glass = (Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.5f +
                           Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.35f +
                           Mathf.Sin(2f * Mathf.PI * f3 * t) * 0.2f) * env;

            // Soft fluid swish
            float fluid = Mathf.Sin(2f * Mathf.PI * 340f * t) * Mathf.Exp(-t * 12f) * 0.25f;

            samples[i] = Mathf.Clamp(glass + fluid, -1f, 1f);
        }

        clip.SetData(samples, 0);
        return clip;
    }
}
