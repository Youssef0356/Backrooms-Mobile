using System;
using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

/// <summary>
/// Allows the player to draw navigational chalk markings (X, Arrow, Checkmark) on walls,
/// matching the iconic survival orientation mechanic in Inside the Backrooms.
/// </summary>
[DisallowMultipleComponent]
public class ChalkController : MonoBehaviour
{
    public static ChalkController Instance { get; private set; }

    public static event Action<int, ChalkTextureBuilder.MarkType> OnChalkUsed;
    public static event Action<ChalkTextureBuilder.MarkType> OnMarkTypeChanged;

    [Header("Chalk Capacity")]
    [SerializeField] private int maxChalkCharges = 35;
    [SerializeField] private int startingChalkCharges = 30;

    [Header("Drawing Raycast")]
    [Tooltip("Maximum distance to wall in meters to draw.")]
    [SerializeField] private float drawRange = 3.5f;
    [Tooltip("Layer mask for Backrooms walls.")]
    [SerializeField] private LayerMask wallMask = ~0;

    private int _remainingChalk;
    private ChalkTextureBuilder.MarkType _currentMark = ChalkTextureBuilder.MarkType.Cross;
    private Camera _playerCamera;
    private AudioSource _audioSource;
    private Transform _marksContainer;

    private bool _canDraw;
    private RaycastHit _currentHit;
    private bool _mobileDrawRequested;
    private bool _mobileCycleRequested;

    public int RemainingChalk => _remainingChalk;
    public int MaxChalkCharges => maxChalkCharges;
    public ChalkTextureBuilder.MarkType CurrentMark => _currentMark;
    public bool CanDrawOnWall => _canDraw && _remainingChalk > 0;
    public RaycastHit CurrentWallHit => _currentHit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        _remainingChalk = Mathf.Clamp(startingChalkCharges, 0, maxChalkCharges);

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D direct in player ears

        GameObject containerObj = new GameObject("ChalkMarkings_Container");
        _marksContainer = containerObj.transform;
    }

    private void Start()
    {
        _playerCamera = GetComponentInChildren<Camera>();
        if (_playerCamera == null) _playerCamera = Camera.main;

        OnChalkUsed?.Invoke(_remainingChalk, _currentMark);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon)
        {
            _canDraw = false;
            return;
        }

        UpdateWallRaycast();
        CheckInput();
    }

    private void UpdateWallRaycast()
    {
        if (_playerCamera == null)
        {
            _playerCamera = Camera.main;
            if (_playerCamera == null)
            {
                _canDraw = false;
                return;
            }
        }

        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, drawRange, wallMask, QueryTriggerInteraction.Ignore);

        if (hits != null && hits.Length > 0)
        {
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || hit.collider.isTrigger) continue;

                // Ignore player's own colliders / character controller
                if (hit.collider.CompareTag("Player") || 
                    hit.transform.root == transform.root || 
                    hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                // Predominantly vertical wall surface (avoid floors or ceilings)
                float angleToUp = Vector3.Angle(hit.normal, Vector3.up);
                if (angleToUp > 55f && angleToUp < 125f)
                {
                    _canDraw = true;
                    _currentHit = hit;
                    return;
                }
            }
        }

        _canDraw = false;
    }

    private void CheckInput()
    {
        Keyboard kb = Keyboard.current;
        Gamepad gp = Gamepad.current;

        // 1. Cycle mark: [V], Gamepad D-pad Left, or Mobile toggle
        bool cycle = _mobileCycleRequested;
        _mobileCycleRequested = false;

        if (kb != null && kb.vKey.wasPressedThisFrame) cycle = true;
        if (gp != null && gp.dpad.left.wasPressedThisFrame) cycle = true;

        if (cycle)
        {
            CycleMarkType();
        }

        // 2. Draw mark: [C], Gamepad D-pad Down, or Mobile tap
        bool draw = _mobileDrawRequested;
        _mobileDrawRequested = false;

        if (kb != null && kb.cKey.wasPressedThisFrame) draw = true;
        if (gp != null && gp.dpad.down.wasPressedThisFrame) draw = true;

        if (draw && CanDrawOnWall)
        {
            DrawMark();
        }
    }

    /// <summary>
    /// Cycles to the next chalk mark symbol: Cross -> Arrow -> Checkmark -> Cross.
    /// </summary>
    public void CycleMarkType()
    {
        switch (_currentMark)
        {
            case ChalkTextureBuilder.MarkType.Cross:
                _currentMark = ChalkTextureBuilder.MarkType.Arrow;
                break;
            case ChalkTextureBuilder.MarkType.Arrow:
                _currentMark = ChalkTextureBuilder.MarkType.Checkmark;
                break;
            default:
                _currentMark = ChalkTextureBuilder.MarkType.Cross;
                break;
        }

        OnMarkTypeChanged?.Invoke(_currentMark);
    }

    /// <summary>
    /// Places a persistent chalk mark decal at the target point on the wall.
    /// </summary>
    public bool DrawMark()
    {
        if (_remainingChalk <= 0 || !_canDraw) return false;

        Vector3 normal = _currentHit.normal;
        // Position comfortably proud of wall face (+0.02m) to prevent Z-fighting and mesh clipping
        Vector3 pos = _currentHit.point + normal * 0.02f;

        GameObject markObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        markObj.name = $"Chalk_{_currentMark}_{_remainingChalk}";
        markObj.layer = 2; // Ignore Raycast
        markObj.transform.SetParent(_marksContainer, true);
        markObj.transform.position = pos;

        // In Unity, PrimitiveType.Quad mesh normal is -Vector3.forward in local space.
        // By aligning transform.forward with -normal (into the wall),
        // the front face normal (-forward) points directly into the room (along +normal).
        Quaternion rot = Quaternion.LookRotation(-normal, Vector3.up);
        markObj.transform.rotation = rot;

        // Subtle organic hand-drawn tilt
        float tilt = UnityEngine.Random.Range(-4f, 4f);
        markObj.transform.Rotate(0f, 0f, tilt, Space.Self);

        // Scale: ~0.55m chalk mark for clear visibility
        markObj.transform.localScale = Vector3.one * 0.55f;

        // Destroy MeshCollider immediately so decals cost 0 physics and don't block subsequent raycasts
        Collider col = markObj.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        // Apply authentic procedural chalk material
        MeshRenderer mr = markObj.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sharedMaterial = ChalkTextureBuilder.GetOrCreateChalkMaterial(_currentMark);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // Play chalk scratch SFX
        AudioClip scratch = ChalkTextureBuilder.GetOrCreateScratchSFX();
        if (scratch != null && _audioSource != null)
        {
            _audioSource.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
            _audioSource.PlayOneShot(scratch, 0.85f * GameSettings.MasterVolume);
        }

        _remainingChalk--;
        OnChalkUsed?.Invoke(_remainingChalk, _currentMark);
        return true;
    }

    /// <summary>
    /// Adds chalk charges (e.g. from collectible chalk boxes).
    /// </summary>
    public void AddChalk(int amount)
    {
        _remainingChalk = Mathf.Clamp(_remainingChalk + amount, 0, maxChalkCharges);
        OnChalkUsed?.Invoke(_remainingChalk, _currentMark);
    }

    public void RequestMobileDraw() => _mobileDrawRequested = true;
    public void RequestMobileCycle() => _mobileCycleRequested = true;
}
