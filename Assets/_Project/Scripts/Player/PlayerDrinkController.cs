using System;
using System.Collections;
using UnityEngine;
using StarterAssets;

/// <summary>
/// Controls the first-person Almond Water drinking animation, audio, and restorative effects.
/// Renders an in-view 3D viewmodel bottle that tilts into the player's view, uncaps, plays liquid
/// drinking sounds and a sigh of relief, reducing hallucinations and restoring stamina.
/// </summary>
[DisallowMultipleComponent]
public class PlayerDrinkController : MonoBehaviour
{
    public static PlayerDrinkController Instance { get; private set; }

    public static event Action OnDrinkStarted;
    public static event Action OnDrinkCompleted;

    [Header("Animation Durations")]
    public float raiseDuration = 0.45f;
    public float chugDuration = 1.35f;
    public float lowerDuration = 0.55f;

    [Header("Viewmodel Poses (Relative to Camera)")]
    public Vector3 restPos = new Vector3(0.24f, -0.55f, 0.42f);
    public Vector3 readyPos = new Vector3(0.16f, -0.22f, 0.38f);
    public Vector3 mouthPos = new Vector3(0.02f, -0.10f, 0.28f);

    public Vector3 restRot = new Vector3(15f, 160f, -8f);
    public Vector3 readyRot = new Vector3(4f, 175f, -4f);
    public Vector3 mouthRot = new Vector3(-58f, 180f, 0f);

    private Transform _cameraTransform;
    private GameObject _viewModelBottle;
    private bool _isDrinking;

    private AudioSource _audioSource;
    private AudioClip _uncapClip;
    private AudioClip _chugClip;
    private AudioClip _sighClip;

    public bool IsDrinking => _isDrinking;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D direct in player ears

        GenerateAudioClips();
    }

    private void Start()
    {
        FindCameraAndSetupViewModel();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void FindCameraAndSetupViewModel()
    {
        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;

        if (cam != null)
        {
            _cameraTransform = cam.transform;
            CreateViewModelBottle(_cameraTransform);
        }
    }

    private void CreateViewModelBottle(Transform parent)
    {
        _viewModelBottle = new GameObject("AlmondWater_ViewModel");
        _viewModelBottle.transform.SetParent(parent, false);
        _viewModelBottle.transform.localPosition = restPos;
        _viewModelBottle.transform.localRotation = Quaternion.Euler(restRot);
        _viewModelBottle.transform.localScale = Vector3.one * 0.95f;

        MeshFilter mf = _viewModelBottle.AddComponent<MeshFilter>();
        mf.sharedMesh = AlmondWaterMeshBuilder.GetOrCreateBottleMesh();

        MeshRenderer mr = _viewModelBottle.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new Material[]
        {
            AlmondWaterMeshBuilder.GetOrCreateBottleMaterial(),
            AlmondWaterMeshBuilder.GetOrCreateLabelMaterial()
        };

        _viewModelBottle.SetActive(false);
    }

    /// <summary>
    /// Initiates drinking Almond Water.
    /// </summary>
    public bool StartDrinking()
    {
        if (_isDrinking || PlayerVentController.IsInVent) return false;

        if (_viewModelBottle == null && _cameraTransform != null)
        {
            CreateViewModelBottle(_cameraTransform);
        }

        if (_viewModelBottle == null) return false;

        StartCoroutine(DrinkRoutine());
        return true;
    }

    private IEnumerator DrinkRoutine()
    {
        _isDrinking = true;
        OnDrinkStarted?.Invoke();

        _viewModelBottle.SetActive(true);
        _viewModelBottle.transform.localPosition = restPos;
        _viewModelBottle.transform.localRotation = Quaternion.Euler(restRot);

        // 1. Raise bottle to chest / mouth & Uncap
        PlaySFX(_uncapClip, 0.9f);
        float elapsed = 0f;
        while (elapsed < raiseDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / raiseDuration);
            _viewModelBottle.transform.localPosition = Vector3.Lerp(restPos, readyPos, t);
            _viewModelBottle.transform.localRotation = Quaternion.Euler(Vector3.Lerp(restRot, readyRot, t));
            yield return null;
        }

        // 2. Tilt into mouth & Chug
        PlaySFX(_chugClip, 1.0f);
        elapsed = 0f;
        bool restored = false;

        while (elapsed < chugDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / 0.35f);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            // Subtle rhythmic drinking tilt wobble
            float gulpWobble = Mathf.Sin(elapsed * 9f) * 2.5f;

            _viewModelBottle.transform.localPosition = Vector3.Lerp(readyPos, mouthPos, smoothT);
            _viewModelBottle.transform.localRotation = Quaternion.Euler(Vector3.Lerp(readyRot, mouthRot, smoothT) + new Vector3(gulpWobble, 0f, 0f));

            // Midway through drinking, apply restorative healing effects
            if (!restored && elapsed >= chugDuration * 0.55f)
            {
                restored = true;
                if (PlayerSanity.Instance != null)
                {
                    PlayerSanity.Instance.RestoreSanity(PlayerSanity.Instance.AlmondWaterRestoreAmount);
                }
                if (PlayerStamina.Instance != null)
                {
                    PlayerStamina.Instance.RestoreStamina(PlayerSanity.Instance != null ? PlayerSanity.Instance.AlmondWaterStaminaBonus : 40f);
                }
            }

            yield return null;
        }

        // 3. Lower bottle & Exhale of relief
        PlaySFX(_sighClip, 0.85f);
        elapsed = 0f;
        while (elapsed < lowerDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / lowerDuration);
            _viewModelBottle.transform.localPosition = Vector3.Lerp(mouthPos, restPos, t);
            _viewModelBottle.transform.localRotation = Quaternion.Euler(Vector3.Lerp(mouthRot, restRot, t));
            yield return null;
        }

        _viewModelBottle.SetActive(false);
        _isDrinking = false;
        OnDrinkCompleted?.Invoke();
    }

    private void PlaySFX(AudioClip clip, float vol)
    {
        if (clip != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(clip, vol);
        }
    }

    private void GenerateAudioClips()
    {
        _uncapClip = CreateUncapSFX();
        _chugClip = CreateChugSFX();
        _sighClip = CreateSighSFX();
    }

    private AudioClip CreateUncapSFX()
    {
        // Realistic metallic twist click and pop
        int rate = 44100;
        int count = (int)(rate * 0.28f);
        AudioClip clip = AudioClip.Create("AlmondUncap", count, 1, rate, false);
        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float twist = (Mathf.Sin(2f * Mathf.PI * 920f * t) * 0.4f +
                           Mathf.Sin(2f * Mathf.PI * 1850f * t) * 0.3f) * Mathf.Exp(-t * 28f);
            float pop = (t > 0.12f) ? Mathf.Sin(2f * Mathf.PI * 320f * (t - 0.12f)) * Mathf.Exp(-(t - 0.12f) * 45f) * 0.6f : 0f;
            data[i] = Mathf.Clamp(twist + pop, -1f, 1f);
        }

        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip CreateChugSFX()
    {
        // 3 realistic liquid swallow / glug gulps
        int rate = 44100;
        float totalSec = 1.35f;
        int count = (int)(rate * totalSec);
        AudioClip clip = AudioClip.Create("AlmondChug", count, 1, rate, false);
        float[] data = new float[count];

        float[] glugStarts = new float[] { 0.12f, 0.52f, 0.92f };

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float sample = 0f;

            foreach (float gs in glugStarts)
            {
                if (t >= gs && t < gs + 0.32f)
                {
                    float localT = t - gs;
                    // Low resonant throat gulp + bubbling fluid slosh
                    float f = Mathf.Lerp(190f, 260f, localT / 0.32f);
                    float gulp = Mathf.Sin(2f * Mathf.PI * f * localT) * Mathf.Sin(Mathf.PI * (localT / 0.32f)) * 0.75f;
                    float bubble = Mathf.Sin(2f * Mathf.PI * (f * 2.3f) * localT) * 0.2f * Mathf.Exp(-localT * 12f);
                    sample += gulp + bubble;
                }
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip CreateSighSFX()
    {
        // Satisfied soft breath / exhale of relief
        int rate = 44100;
        int count = (int)(rate * 0.65f);
        AudioClip clip = AudioClip.Create("AlmondSigh", count, 1, rate, false);
        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float noise = (UnityEngine.Random.value * 2f - 1f) * 0.35f;
            float envelope = Mathf.Sin(Mathf.PI * (t / 0.65f));
            float lowBreath = Mathf.Sin(2f * Mathf.PI * 140f * t) * 0.15f;
            data[i] = (noise + lowBreath) * envelope;
        }

        clip.SetData(data, 0);
        return clip;
    }
}
