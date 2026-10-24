using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A psychological phantom projection of the entity wandering the Backrooms.
/// Glides/walks down distant corridors as the player's sanity degrades.
/// Dissolves into black smoke if approached, illuminated directly, or when its time expires.
/// Has no physical presence or collision.
/// </summary>
public class PhantomShadowWalker : MonoBehaviour
{
    private float _speed = 1.35f;
    private float _lifetime = 4.5f;
    private float _dissolveDist = 4.2f;

    private float _timer;
    private bool _isDissolving;
    private Transform _playerTransform;
    private Camera _playerCamera;
    private List<Renderer> _renderers = new List<Renderer>();
    private List<Material> _instancedMaterials = new List<Material>();

    private static AudioClip _dissolveClip;

    public void Initialize(Transform player, float speed, float lifetime, Material shadowMaterial)
    {
        _playerTransform = player;
        if (_playerTransform != null)
        {
            _playerCamera = _playerTransform.GetComponentInChildren<Camera>();
        }
        if (_playerCamera == null) _playerCamera = Camera.main;

        _speed = speed;
        _lifetime = lifetime;

        // Apply shadow silhouette material to all child renderers
        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in rends)
        {
            if (r == null) continue;
            Material instMat = new Material(shadowMaterial);
            _instancedMaterials.Add(instMat);
            r.material = instMat;
            _renderers.Add(r);
        }

        // Ensure all colliders and physics components are removed
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        foreach (Collider c in cols) Destroy(c);

        Rigidbody[] rbs = GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody rb in rbs) Destroy(rb);

        UnityEngine.AI.NavMeshAgent[] agents = GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true);
        foreach (var a in agents) Destroy(a);

        // Setup Animator if present
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.applyRootMotion = false;
            // Play Walk animation
            anim.SetFloat("Speed", 1.0f);
            anim.SetBool("IsChasing", false);
        }
    }

    private void Update()
    {
        if (_isDissolving) return;

        float dt = Time.deltaTime;
        _timer += dt;

        // Walk forward smoothly
        transform.position += transform.forward * (_speed * dt);

        // Check if player is too close
        if (_playerTransform != null)
        {
            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            if (dist < _dissolveDist)
            {
                StartDissolve();
                return;
            }

            // Check if player shines flashlight directly at phantom
            if (_playerCamera != null)
            {
                Vector3 toPhantom = (transform.position + Vector3.up * 1.2f) - _playerCamera.transform.position;
                if (toPhantom.magnitude < 16f && Vector3.Dot(_playerCamera.transform.forward, toPhantom.normalized) > 0.88f)
                {
                    // Flashlight gaze triggers rapid dissolution
                    _timer += dt * 1.5f;
                }
            }
        }

        if (_timer >= _lifetime)
        {
            StartDissolve();
        }
    }

    private void StartDissolve()
    {
        if (_isDissolving) return;
        _isDissolving = true;
        StartCoroutine(DissolveRoutine());
    }

    private IEnumerator DissolveRoutine()
    {
        PlayDissolveSound(transform.position + Vector3.up * 1.2f);

        float duration = 0.65f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(0.65f, 0f, elapsed / duration);

            foreach (Material m in _instancedMaterials)
            {
                if (m != null)
                {
                    Color c = m.color;
                    c.a = alpha;
                    m.color = c;
                }
            }

            // Subtle rise and disperse
            transform.position += Vector3.up * (Time.deltaTime * 0.45f);
            yield return null;
        }

        Destroy(gameObject);
    }

    private static void PlayDissolveSound(Vector3 pos)
    {
        if (_dissolveClip == null)
        {
            _dissolveClip = CreateDissolveAudio();
        }

        GameObject audioObj = new GameObject("PhantomDissolve_SFX");
        audioObj.transform.position = pos;
        AudioSource src = audioObj.AddComponent<AudioSource>();
        src.clip = _dissolveClip;
        src.volume = 0.55f;
        src.spatialBlend = 0.85f;
        src.minDistance = 2f;
        src.maxDistance = 25f;
        src.Play();
        Destroy(audioObj, 1.8f);
    }

    private static AudioClip CreateDissolveAudio()
    {
        // Haunting shadowy whoosh + reverse breath
        int rate = 44100;
        int count = (int)(rate * 0.7f);
        AudioClip clip = AudioClip.Create("PhantomDissolve", count, 1, rate, false);
        float[] samples = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float progress = t / 0.7f;
            float noise = (Random.value * 2f - 1f) * 0.4f;
            float subHum = Mathf.Sin(2f * Mathf.PI * 85f * t) * 0.35f;
            float env = Mathf.Sin(Mathf.PI * progress);
            samples[i] = (noise + subHum) * env * (1f - progress * 0.5f);
        }

        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        foreach (Material m in _instancedMaterials)
        {
            if (m != null) Destroy(m);
        }
    }
}
