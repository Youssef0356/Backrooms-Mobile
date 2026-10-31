using UnityEngine;

/// <summary>
/// Phase 3 (Level 0 Overhaul): Kane Pixels Found-Footage Procedural Twitch Layer for Bacteria.
/// Applies erratic, insectoid micro-jerks and asymmetric head/spine/shoulder spasms in LateUpdate
/// on top of the Humanoid Animator states (Idle (2), Injured Run, Running, Zombie Neck Bite).
/// </summary>
[DisallowMultipleComponent]
public class BacteriaTwitch : MonoBehaviour
{
    [Header("Twitch Intensity")]
    [Tooltip("Base angular twitch amplitude in degrees while idle or wandering.")]
    public float baseHeadTwitch = 9.5f;
    [Tooltip("Amplified angular twitch in degrees during shock-stare or active chase.")]
    public float chaseHeadTwitch = 18.0f;
    [Tooltip("Spine and shoulder spasm amplitude in degrees.")]
    public float spineTwitch = 6.0f;

    private Animator anim;
    private VillainAI villainAI;

    private Transform headBone;
    private Transform neckBone;
    private Transform upperChestBone;
    private Transform leftShoulderBone;
    private Transform rightShoulderBone;

    private Quaternion currentHeadOffset = Quaternion.identity;
    private Quaternion targetHeadOffset = Quaternion.identity;
    private Quaternion currentSpineOffset = Quaternion.identity;
    private Quaternion targetSpineOffset = Quaternion.identity;

    private float nextJerkTime;
    private float shockBoostTimer;

    public void TriggerShockSpasm(float duration = 0.9f)
    {
        shockBoostTimer = duration;
        nextJerkTime = 0f;
    }

    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        villainAI = GetComponent<VillainAI>();
        ResolveBones();
    }

    private void ResolveBones()
    {
        if (anim != null && anim.isHuman)
        {
            headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            neckBone = anim.GetBoneTransform(HumanBodyBones.Neck);
            upperChestBone = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            leftShoulderBone = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightShoulderBone = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }

        if (headBone == null)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            foreach (var t in children)
            {
                string n = t.name.ToLowerInvariant();
                if (headBone == null && n.Contains("head") && !n.Contains("end")) headBone = t;
                else if (neckBone == null && n.Contains("neck")) neckBone = t;
                else if (upperChestBone == null && (n.Contains("spine2") || n.Contains("spine1"))) upperChestBone = t;
            }
        }
    }

    private void LateUpdate()
    {
        if (headBone == null)
        {
            ResolveBones();
            if (headBone == null) return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (shockBoostTimer > 0f) shockBoostTimer -= dt;

        bool isChasing = (villainAI != null && villainAI.CurrentState == VillainAI.State.Chase);
        bool isShocked = shockBoostTimer > 0f;

        if (Time.time >= nextJerkTime)
        {
            float interval = isShocked ? Random.Range(0.04f, 0.09f) :
                             isChasing ? Random.Range(0.06f, 0.16f) :
                                         Random.Range(0.12f, 0.45f);
            nextJerkTime = Time.time + interval;

            float headAmp = isShocked ? chaseHeadTwitch * 1.45f : (isChasing ? chaseHeadTwitch : baseHeadTwitch);
            float spineAmp = isShocked ? spineTwitch * 1.5f : (isChasing ? spineTwitch * 1.2f : spineTwitch);

            // Occasional sharp 90% snap vs subtle tremor
            if (Random.value < 0.35f || isShocked)
            {
                targetHeadOffset = Quaternion.Euler(
                    Random.Range(-headAmp, headAmp),
                    Random.Range(-headAmp * 0.8f, headAmp * 0.8f),
                    Random.Range(-headAmp * 1.25f, headAmp * 1.25f) // eerie sideways head cocking
                );
                targetSpineOffset = Quaternion.Euler(
                    Random.Range(-spineAmp, spineAmp),
                    Random.Range(-spineAmp * 0.6f, spineAmp * 0.6f),
                    Random.Range(-spineAmp, spineAmp)
                );
            }
            else
            {
                targetHeadOffset = Quaternion.identity;
                targetSpineOffset = Quaternion.identity;
            }
        }

        float snapSpeed = isShocked ? 36f : 24f;
        currentHeadOffset = Quaternion.Slerp(currentHeadOffset, targetHeadOffset, dt * snapSpeed);
        currentSpineOffset = Quaternion.Slerp(currentSpineOffset, targetSpineOffset, dt * (snapSpeed * 0.75f));

        if (upperChestBone != null)
        {
            upperChestBone.localRotation = upperChestBone.localRotation * currentSpineOffset;
        }
        if (neckBone != null)
        {
            neckBone.localRotation = neckBone.localRotation * Quaternion.Slerp(Quaternion.identity, currentHeadOffset, 0.45f);
        }
        if (headBone != null)
        {
            headBone.localRotation = headBone.localRotation * currentHeadOffset;
        }
    }
}
