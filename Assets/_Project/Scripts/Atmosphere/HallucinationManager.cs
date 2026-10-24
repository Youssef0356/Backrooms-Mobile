using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using StarterAssets;

/// <summary>
/// Controls all psychological hallucination manifestations as the player's sanity degrades:
/// 1. Spawning Phantom Shadow Walkers (dark silhouettes of the villain walking down distant hallways).
/// 2. Auditory Hallucinations: low eerie sub-bass dread drone, 3D spatial whispers, phantom footsteps.
/// 3. Hallucinatory Heartbeat and Tinnitus when sanity enters critical psychosis.
/// 4. Subtle visual edge vignette and mental instability.
/// </summary>
[DisallowMultipleComponent]
public class HallucinationManager : MonoBehaviour
{
    public static HallucinationManager Instance { get; private set; }

    [Header("Shadow Walker Spawning")]
    [Tooltip("Minimum distance in front of player to spawn phantom silhouettes.")]
    public float minSpawnDist = 12.0f;
    [Tooltip("Maximum distance in front of player to spawn phantom silhouettes.")]
    public float maxSpawnDist = 20.0f;

    [Tooltip("Base interval between shadow spawns at high hallucination (seconds).")]
    public float minSpawnInterval = 18.0f;
    [Tooltip("Base interval between shadow spawns at moderate hallucination (seconds).")]
    public float maxSpawnInterval = 48.0f;

    [Header("Auditory Hallucinations")]
    [Tooltip("Interval between random eerie whispers or phantom footsteps (seconds).")]
    public Vector2 audioEventInterval = new Vector2(16.0f, 38.0f);

    private Transform _playerTransform;
    private Camera _playerCamera;
    private GameObject _villainPrefabOrTemplate;

    private Material _shadowMaterial;
    private float _shadowSpawnTimer;
    private float _audioEventTimer;

    // Audio sources
    private AudioSource _dreadDroneSource;
    private AudioSource _heartbeatSource;
    private AudioSource _tinnitusSource;

    private AudioClip _droneClip;
    private AudioClip _heartbeatClip;
    private AudioClip _tinnitusClip;
    private List<AudioClip> _whisperClips = new List<AudioClip>();
    private List<AudioClip> _phantomStepClips = new List<AudioClip>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        SetupShadowMaterial();
        SetupAudioComponents();
    }

    private void Start()
    {
        FindPlayerAndVillain();
        _shadowSpawnTimer = Random.Range(minSpawnInterval, maxSpawnInterval);
        _audioEventTimer = Random.Range(audioEventInterval.x, audioEventInterval.y);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_shadowMaterial != null) Destroy(_shadowMaterial);
    }

    private void FindPlayerAndVillain()
    {
        FirstPersonController fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null)
        {
            _playerTransform = fpc.transform;
            _playerCamera = fpc.GetComponentInChildren<Camera>();
        }
        if (_playerCamera == null) _playerCamera = Camera.main;

        // Try to get villain template from VillainAI
        VillainAI villain = VillainAI.Instance;
        if (villain != null)
        {
            _villainPrefabOrTemplate = villain.gameObject;
        }
    }

    private void Update()
    {
        if (PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon)
            return;

        if (_playerTransform == null || _playerCamera == null)
        {
            FindPlayerAndVillain();
            if (_playerTransform == null) return;
        }

        float hallucination = PlayerSanity.Instance != null ? PlayerSanity.Instance.HallucinationLevel : 0f;
        float dt = Time.deltaTime;

        UpdateContinuousAudio(hallucination);

        // Hallucinations only activate once sanity starts slipping (> 22% hallucination)
        if (hallucination < 0.22f) return;

        // 1. Phantom Shadow Walker Spawning
        _shadowSpawnTimer -= dt;
        if (_shadowSpawnTimer <= 0f)
        {
            // Interval gets shorter as hallucination increases
            float interval = Mathf.Lerp(maxSpawnInterval, minSpawnInterval, (hallucination - 0.22f) / 0.78f);
            _shadowSpawnTimer = interval + Random.Range(-4f, 4f);

            TrySpawnShadowWalker(hallucination);
        }

        // 2. Spatial Eerie Audio Events (Whispers, Phantom Footsteps)
        _audioEventTimer -= dt;
        if (_audioEventTimer <= 0f)
        {
            float audioInterval = Mathf.Lerp(audioEventInterval.y, audioEventInterval.x, hallucination);
            _audioEventTimer = audioInterval + Random.Range(-3f, 5f);

            TriggerAuditoryHallucination(hallucination);
        }
    }

    private void UpdateContinuousAudio(float hallucination)
    {
        // 1. Dread Sub-bass Drone: scales from 0 to 0.40 volume as hallucination rises
        if (_dreadDroneSource != null)
        {
            float targetDroneVol = hallucination > 0.15f ? Mathf.Lerp(0f, 0.42f, (hallucination - 0.15f) / 0.85f) : 0f;
            _dreadDroneSource.volume = Mathf.MoveTowards(_dreadDroneSource.volume, targetDroneVol, Time.deltaTime * 0.25f);
            if (!_dreadDroneSource.isPlaying && _dreadDroneSource.volume > 0.01f)
            {
                _dreadDroneSource.Play();
            }
        }

        // 2. Severe Psychosis Heartbeat & Tinnitus (Sanity < 22%, Hallucination > 0.78)
        if (_heartbeatSource != null)
        {
            float targetHeartbeatVol = hallucination > 0.75f ? Mathf.Lerp(0f, 0.55f, (hallucination - 0.75f) / 0.25f) : 0f;
            _heartbeatSource.volume = Mathf.MoveTowards(_heartbeatSource.volume, targetHeartbeatVol, Time.deltaTime * 0.4f);
            if (!_heartbeatSource.isPlaying && _heartbeatSource.volume > 0.01f)
            {
                _heartbeatSource.Play();
            }
        }

        if (_tinnitusSource != null)
        {
            float targetTinnitusVol = hallucination > 0.85f ? Mathf.Lerp(0f, 0.25f, (hallucination - 0.85f) / 0.15f) : 0f;
            _tinnitusSource.volume = Mathf.MoveTowards(_tinnitusSource.volume, targetTinnitusVol, Time.deltaTime * 0.3f);
            if (!_tinnitusSource.isPlaying && _tinnitusSource.volume > 0.01f)
            {
                _tinnitusSource.Play();
            }
        }
    }

    private void TrySpawnShadowWalker(float hallucination)
    {
        if (_playerCamera == null) return;

        // Choose a spot ahead of the player in their general forward field of view
        float angle = Random.Range(-35f, 35f);
        Quaternion rot = Quaternion.Euler(0f, angle, 0f);
        Vector3 dir = rot * _playerCamera.transform.forward;
        dir.y = 0f;
        dir.Normalize();

        float dist = Random.Range(minSpawnDist, maxSpawnDist);
        Vector3 targetSpot = _playerTransform.position + dir * dist;

        // Snap to valid NavMesh or floor
        if (NavMesh.SamplePosition(targetSpot, out NavMeshHit hit, 4.5f, NavMesh.AllAreas))
        {
            targetSpot = hit.position;
        }
        else
        {
            return;
        }

        // Orient shadow: either walking away down hallway, or crossing hallway
        bool walkAcross = Random.value < 0.45f;
        Quaternion shadowRot;
        if (walkAcross)
        {
            // Perpendicular to player gaze
            Vector3 side = Vector3.Cross(dir, Vector3.up) * (Random.value < 0.5f ? 1f : -1f);
            shadowRot = Quaternion.LookRotation(side, Vector3.up);
        }
        else
        {
            // Walking away into the distance
            shadowRot = Quaternion.LookRotation(dir, Vector3.up);
        }

        SpawnShadowInstance(targetSpot, shadowRot);
    }

    private void SpawnShadowInstance(Vector3 position, Quaternion rotation)
    {
        GameObject shadowObj = null;

        // Check if we can clone from villain template
        if (_villainPrefabOrTemplate == null && VillainAI.Instance != null)
        {
            _villainPrefabOrTemplate = VillainAI.Instance.gameObject;
        }

        if (_villainPrefabOrTemplate != null)
        {
            shadowObj = Instantiate(_villainPrefabOrTemplate, position, rotation);
            shadowObj.name = "PhantomShadowWalker";
            shadowObj.SetActive(true);

            // Strip unnecessary scripts & agents
            VillainAI ai = shadowObj.GetComponent<VillainAI>();
            if (ai != null) Destroy(ai);

            NavMeshAgent agent = shadowObj.GetComponent<NavMeshAgent>();
            if (agent != null) Destroy(agent);

            AudioSource[] audioSources = shadowObj.GetComponentsInChildren<AudioSource>(true);
            foreach (var a in audioSources) Destroy(a);
        }
        else
        {
            // Fallback: create procedural humanoid silhouette
            shadowObj = CreateFallbackSilhouette();
            shadowObj.transform.position = position;
            shadowObj.transform.rotation = rotation;
        }

        // Ensure proper scale for Clark model
        if (shadowObj.transform.localScale.x > 0.5f)
        {
            shadowObj.transform.localScale = new Vector3(0.16f, 0.16f, 0.16f);
        }

        // Add PhantomShadowWalker controller
        PhantomShadowWalker walker = shadowObj.AddComponent<PhantomShadowWalker>();
        walker.Initialize(_playerTransform, 1.35f, 4.8f, _shadowMaterial);
    }

    private GameObject CreateFallbackSilhouette()
    {
        GameObject root = new GameObject("PhantomShadow_Fallback");

        // Capsule body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1.0f, 0f);
        body.transform.localScale = new Vector3(0.45f, 0.95f, 0.35f);

        // Head sphere
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.78f, 0f);
        head.transform.localScale = Vector3.one * 0.32f;

        Collider[] cols = root.GetComponentsInChildren<Collider>();
        foreach (var c in cols) Destroy(c);

        return root;
    }

    private void TriggerAuditoryHallucination(float hallucination)
    {
        if (_playerTransform == null) return;

        // Pick random offset around player (2 to 6 metres away)
        Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(2.5f, 6.0f);
        Vector3 soundPos = _playerTransform.position + new Vector3(circle.x, 1.5f, circle.y);

        // Roll event type: spatial whisper vs phantom steps
        bool isWhisper = Random.value < 0.55f;

        GameObject audioObj = new GameObject("HallucinationAudio_Spatial");
        audioObj.transform.position = soundPos;

        AudioSource src = audioObj.AddComponent<AudioSource>();
        src.spatialBlend = 0.85f;
        src.minDistance = 1.5f;
        src.maxDistance = 18f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;

        if (isWhisper && _whisperClips.Count > 0)
        {
            AudioClip clip = _whisperClips[Random.Range(0, _whisperClips.Count)];
            src.clip = clip;
            src.volume = Mathf.Lerp(0.35f, 0.75f, hallucination);
            src.pitch = Random.Range(0.85f, 1.05f);
            src.Play();
            Destroy(audioObj, clip.length + 0.5f);
        }
        else if (_phantomStepClips.Count > 0)
        {
            AudioClip clip = _phantomStepClips[Random.Range(0, _phantomStepClips.Count)];
            src.clip = clip;
            src.volume = Mathf.Lerp(0.4f, 0.8f, hallucination);
            src.pitch = Random.Range(0.88f, 1.12f);
            src.Play();
            Destroy(audioObj, clip.length + 0.5f);
        }
        else
        {
            Destroy(audioObj);
        }
    }

    private void SetupShadowMaterial()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Standard");

        _shadowMaterial = new Material(s) { name = "Phantom_Shadow_Material" };

        // Pitch black translucent silhouette with soft edge
        _shadowMaterial.color = new Color(0.015f, 0.015f, 0.02f, 0.65f);

        // Set to Transparent surface mode
        _shadowMaterial.SetFloat("_Surface", 1); // 1 = Transparent in URP
        _shadowMaterial.SetFloat("_Blend", 0);   // Alpha blend
        _shadowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _shadowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _shadowMaterial.SetInt("_ZWrite", 0);
        _shadowMaterial.renderQueue = 3050;

        if (_shadowMaterial.HasProperty("_Smoothness")) _shadowMaterial.SetFloat("_Smoothness", 0.05f);
        if (_shadowMaterial.HasProperty("_Metallic")) _shadowMaterial.SetFloat("_Metallic", 0f);
    }

    private void SetupAudioComponents()
    {
        // 1. Continuous Sub-bass Dread Drone (52 Hz binaural hum)
        _droneClip = CreateSubBassDroneClip();
        _dreadDroneSource = gameObject.AddComponent<AudioSource>();
        _dreadDroneSource.clip = _droneClip;
        _dreadDroneSource.loop = true;
        _dreadDroneSource.spatialBlend = 0f;
        _dreadDroneSource.volume = 0f;
        _dreadDroneSource.playOnAwake = false;

        // 2. Severe Heartbeat
        _heartbeatClip = CreateHeartbeatClip();
        _heartbeatSource = gameObject.AddComponent<AudioSource>();
        _heartbeatSource.clip = _heartbeatClip;
        _heartbeatSource.loop = true;
        _heartbeatSource.spatialBlend = 0f;
        _heartbeatSource.volume = 0f;
        _heartbeatSource.playOnAwake = false;

        // 3. Tinnitus ringing
        _tinnitusClip = CreateTinnitusClip();
        _tinnitusSource = gameObject.AddComponent<AudioSource>();
        _tinnitusSource.clip = _tinnitusClip;
        _tinnitusSource.loop = true;
        _tinnitusSource.spatialBlend = 0f;
        _tinnitusSource.volume = 0f;
        _tinnitusSource.playOnAwake = false;

        // 4. Spatial Whispers & Phantom Steps
        GenerateProceduralHallucinationClips();
    }

    private AudioClip CreateSubBassDroneClip()
    {
        // 4.0s seamless looping 52Hz sub-bass binaural hum with low slow harmonic modulation
        int rate = 44100;
        float dur = 4.0f;
        int count = (int)(rate * dur);
        AudioClip clip = AudioClip.Create("SubBassDreadDrone", count, 1, rate, false);
        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float f0 = 52.0f;
            float f1 = 54.5f; // binaural beat frequency
            float slowMod = Mathf.Sin(2f * Mathf.PI * (1f / dur) * t);

            float bass = Mathf.Sin(2f * Mathf.PI * f0 * t) * 0.6f +
                         Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.35f +
                         Mathf.Sin(2f * Mathf.PI * 104f * t) * (0.15f + slowMod * 0.05f);

            data[i] = Mathf.Clamp(bass * 0.75f, -1f, 1f);
        }

        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip CreateHeartbeatClip()
    {
        // 1.2s repeating muffled thud-thud heartbeat
        int rate = 44100;
        float dur = 1.15f;
        int count = (int)(rate * dur);
        AudioClip clip = AudioClip.Create("HallucinationHeartbeat", count, 1, rate, false);
        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float sample = 0f;

            // Lub (first beat at t = 0.08)
            if (t >= 0.08f && t < 0.32f)
            {
                float lt = t - 0.08f;
                sample += Mathf.Sin(2f * Mathf.PI * 65f * lt) * Mathf.Exp(-lt * 16f) * 0.85f;
            }

            // Dub (second beat at t = 0.38)
            if (t >= 0.38f && t < 0.60f)
            {
                float lt = t - 0.38f;
                sample += Mathf.Sin(2f * Mathf.PI * 75f * lt) * Mathf.Exp(-lt * 20f) * 0.65f;
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip CreateTinnitusClip()
    {
        // 3.0s looping high-pitch 7200Hz ringing
        int rate = 44100;
        float dur = 3.0f;
        int count = (int)(rate * dur);
        AudioClip clip = AudioClip.Create("TinnitusRinging", count, 1, rate, false);
        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float sine = Mathf.Sin(2f * Mathf.PI * 7200f * t) * 0.3f;
            float sine2 = Mathf.Sin(2f * Mathf.PI * 7215f * t) * 0.2f;
            data[i] = sine + sine2;
        }

        clip.SetData(data, 0);
        return clip;
    }

    private void GenerateProceduralHallucinationClips()
    {
        int rate = 44100;

        // 1. Whispers (synthesized raspy airy whispers)
        for (int w = 0; w < 3; w++)
        {
            float dur = 1.4f + w * 0.35f;
            int count = (int)(rate * dur);
            AudioClip clip = AudioClip.Create($"Whisper_{w}", count, 1, rate, false);
            float[] data = new float[count];

            float formant = 700f + w * 250f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / rate;
                float noise = (Random.value * 2f - 1f) * 0.5f;
                float resonance = Mathf.Sin(2f * Mathf.PI * formant * t) * noise;
                float env = Mathf.Sin(Mathf.PI * (t / dur));
                data[i] = resonance * env * 0.6f;
            }

            clip.SetData(data, 0);
            _whisperClips.Add(clip);
        }

        // 2. Phantom Footsteps (distant heavy boots echoing)
        for (int s = 0; s < 2; s++)
        {
            float dur = 1.6f;
            int count = (int)(rate * dur);
            AudioClip clip = AudioClip.Create($"PhantomStepSequence_{s}", count, 1, rate, false);
            float[] data = new float[count];

            float[] stepTimes = new float[] { 0.1f, 0.6f, 1.1f };
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / rate;
                float sample = 0f;

                foreach (float st in stepTimes)
                {
                    if (t >= st && t < st + 0.35f)
                    {
                        float lt = t - st;
                        float thud = Mathf.Sin(2f * Mathf.PI * 90f * lt) * Mathf.Exp(-lt * 18f) * 0.7f;
                        float scuff = (Random.value * 2f - 1f) * 0.25f * Mathf.Exp(-lt * 25f);
                        sample += thud + scuff;
                    }
                }

                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            clip.SetData(data, 0);
            _phantomStepClips.Add(clip);
        }
    }
}
