using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Procedural 4-Legged (Quadruped) Gallop & Stride Controller for the Smily Horror Monster / Dog.
/// Dynamically animates the 4 legs, flexing spine, bobbing head, and snapping jaws
/// to create a terrifying, organic predatory gallop that automatically synchronizes
/// with the monster's ground movement speed.
/// </summary>
[DisallowMultipleComponent]
public class SmilyMonsterGait : MonoBehaviour
{
    [Header("Movement & Speed")]
    [Tooltip("Movement speed at which the gallop reaches its maximum stride frequency.")]
    public float maxSpeed = 4.2f;
    [Tooltip("Stride cycle frequency at walking/stalking speed (cycles/sec).")]
    public float walkFrequency = 1.25f;
    [Tooltip("Stride cycle frequency at full sprint/chase speed (cycles/sec).")]
    public float sprintFrequency = 1.65f;

    [Header("Spine & Body Dynamics")]
    [Tooltip("Vertical body heave (hips bobbing up and down) during the gallop stride.")]
    public float verticalHeave = 0.045f;
    [Tooltip("Spine pitch flexing angle (degrees) during spinal expansion and coiling.")]
    public float spinePitchFlex = 14f;
    [Tooltip("Hips roll and sway angle (degrees) as diagonal pairs plant.")]
    public float bodyRollSway = 5.5f;

    [Header("Front Legs (Forelimbs)")]
    [Tooltip("Shoulder/Upper Arm forward-backward stride swing (degrees).")]
    public float frontArmPitchSwing = 34f;
    [Tooltip("Elbow shock-absorption and recovery flexion (degrees).")]
    public float frontElbowFlex = 28f;
    [Tooltip("Paw/Hand pitch angle to keep pads facing ground (degrees).")]
    public float frontPawPitch = 18f;

    [Header("Rear Legs (Hindlimbs)")]
    [Tooltip("Thigh/Upper Leg driving swing angle (degrees).")]
    public float rearLegPitchSwing = 38f;
    [Tooltip("Knee/Calf coiling flexion (degrees).")]
    public float rearKneeFlex = 36f;
    [Tooltip("Rear foot/hock flexion and push-off angle (degrees).")]
    public float rearFootPitch = 22f;

    [Header("Head & Jaws")]
    [Tooltip("Head pitch bobbing angle during strides (degrees).")]
    public float headBobAngle = 10f;
    [Tooltip("Degrees the jaw snaps open and shut during aggressive running.")]
    public float jawSnapAngle = 26f;

    [Header("Attack Pounce")]
    [Tooltip("Duration of the attack pounce animation (seconds).")]
    public float pounceDuration = 0.75f;

    [Header("Look Target")]
    [Tooltip("Target (player camera) the creature's head will actively look at during pursuit.")]
    public Transform lookTarget;

    [Header("Bone Transforms")]
    private Transform hips;         // spinebase_01
    private Transform spineMid;     // belly_02
    private Transform chest;        // chest_03
    private Transform neck;         // neck_04
    private Transform head;         // head_05
    private Transform jaw;          // jaw_06

    private Transform frontArmL;    // Arm.L_09
    private Transform frontForearmL;// Forearm.L_010
    private Transform frontHandL;   // Hand.L_011

    private Transform frontArmR;    // Arm.R_022
    private Transform frontForearmR;// Forearm.R_023
    private Transform frontHandR;   // Hand.R_024

    private Transform rearLegL;     // leg.L_036
    private Transform rearCalfL;    // sheen.L_037
    private Transform rearFootL;    // foot.L_038

    private Transform rearLegR;     // leg.R_041
    private Transform rearCalfR;    // sheen.R_042
    private Transform rearFootR;    // foot.R_043

    // Rest/Reference Transforms
    private Vector3 hipsBasePos;
    private Quaternion hipsBaseRot;
    private Quaternion spineMidBaseRot;
    private Quaternion chestBaseRot;
    private Quaternion neckBaseRot;
    private Quaternion headBaseRot;
    private Quaternion jawBaseRot;

    private Quaternion frontArmLBaseRot;
    private Quaternion frontForearmLBaseRot;
    private Quaternion frontHandLBaseRot;

    private Quaternion frontArmRBaseRot;
    private Quaternion frontForearmRBaseRot;
    private Quaternion frontHandRBaseRot;

    private Quaternion rearLegLBaseRot;
    private Quaternion rearCalfLBaseRot;
    private Quaternion rearFootLBaseRot;

    private Quaternion rearLegRBaseRot;
    private Quaternion rearCalfRBaseRot;
    private Quaternion rearFootRBaseRot;

    // State
    private NavMeshAgent navAgent;
    private Vector3 lastPosition;
    private float currentSpeed;
    private float strideCycle;
    private float gaitWeight; // 0 = idle pose, 1 = full gallop

    private bool isPouncing;
    private float pounceTimer;

    // Kill Jumpscare Animation State
    private bool isKillMauling;
    private float killMaulTimer;
    private Transform killTargetCam;

    public Transform Head => head;
    public Transform Jaw => jaw;
    public bool IsKillMauling => isKillMauling;

    /// <summary>
    /// Triggers an intense, cinematic jumpscare kill animation:
    /// Phase 1: Beast rears up on hind legs, unhinging jaws and towering over player.
    /// Phase 2: Savage forward bite lunge directly into camera with violent snaps.
    /// Phase 3: Pins player to floor, towering and growling inches from camera.
    /// </summary>
    public void TriggerKillJumpscare(Transform playerCam)
    {
        isKillMauling = true;
        killMaulTimer = 0f;
        killTargetCam = playerCam;
        isPouncing = false;
        gaitWeight = 1f;
    }

    private void Awake()
    {
        ResolveBones();
        navAgent = GetComponent<NavMeshAgent>();
        if (navAgent == null) navAgent = GetComponentInParent<NavMeshAgent>();
        lastPosition = transform.position;
    }

    private void Start()
    {
        CacheRestPoses();
    }

    /// <summary>
    /// Finds all creature bones in hierarchy regardless of nesting depth.
    /// </summary>
    private void ResolveBones()
    {
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        foreach (Transform t in allChildren)
        {
            string n = t.name;
            if (n.Contains("spinebase_01")) hips = t;
            else if (n.Contains("belly_02")) spineMid = t;
            else if (n.Contains("chest_03")) chest = t;
            else if (n.Contains("neck_04")) neck = t;
            else if (n.Contains("head_05")) head = t;
            else if (n.Contains("jaw_06")) jaw = t;

            else if (n.Contains("Arm.L_09")) frontArmL = t;
            else if (n.Contains("Forearm.L_010")) frontForearmL = t;
            else if (n.Contains("Hand.L_011")) frontHandL = t;

            else if (n.Contains("Arm.R_022")) frontArmR = t;
            else if (n.Contains("Forearm.R_023")) frontForearmR = t;
            else if (n.Contains("Hand.R_024")) frontHandR = t;

            else if (n.Contains("leg.L_036")) rearLegL = t;
            else if (n.Contains("sheen.L_037")) rearCalfL = t;
            else if (n.Contains("foot.L_038")) rearFootL = t;

            else if (n.Contains("leg.R_041")) rearLegR = t;
            else if (n.Contains("sheen.R_042")) rearCalfR = t;
            else if (n.Contains("foot.R_043")) rearFootR = t;
        }
    }

    private void CacheRestPoses()
    {
        if (hips != null) { hipsBasePos = hips.localPosition; hipsBaseRot = hips.localRotation; }
        if (spineMid != null) spineMidBaseRot = spineMid.localRotation;
        if (chest != null) chestBaseRot = chest.localRotation;
        if (neck != null) neckBaseRot = neck.localRotation;
        if (head != null) headBaseRot = head.localRotation;
        if (jaw != null) jawBaseRot = jaw.localRotation;

        if (frontArmL != null) frontArmLBaseRot = frontArmL.localRotation;
        if (frontForearmL != null) frontForearmLBaseRot = frontForearmL.localRotation;
        if (frontHandL != null) frontHandLBaseRot = frontHandL.localRotation;

        if (frontArmR != null) frontArmRBaseRot = frontArmR.localRotation;
        if (frontForearmR != null) frontForearmRBaseRot = frontForearmR.localRotation;
        if (frontHandR != null) frontHandRBaseRot = frontHandR.localRotation;

        if (rearLegL != null) rearLegLBaseRot = rearLegL.localRotation;
        if (rearCalfL != null) rearCalfLBaseRot = rearCalfL.localRotation;
        if (rearFootL != null) rearFootLBaseRot = rearFootL.localRotation;

        if (rearLegR != null) rearLegRBaseRot = rearLegR.localRotation;
        if (rearCalfR != null) rearCalfRBaseRot = rearCalfR.localRotation;
        if (rearFootR != null) rearFootRBaseRot = rearFootR.localRotation;
    }

    private void Update()
    {
        // 1. Calculate ground movement speed
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float measuredSpeed = 0f;
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
        {
            measuredSpeed = navAgent.velocity.magnitude;
        }
        else
        {
            measuredSpeed = Vector3.Distance(transform.position, lastPosition) / dt;
            lastPosition = transform.position;
        }

        currentSpeed = Mathf.Lerp(currentSpeed, measuredSpeed, dt * 10f);

        // 2. Adjust Gallop / Stride Cycle
        float speedRatio = Mathf.Clamp01(currentSpeed / Mathf.Max(0.1f, maxSpeed));
        float targetWeight = currentSpeed > 0.12f ? 1f : 0f;
        gaitWeight = Mathf.MoveTowards(gaitWeight, targetWeight, dt * (currentSpeed > 0.12f ? 6.5f : 4.0f));

        if (gaitWeight > 0.01f)
        {
            float freq = Mathf.Lerp(walkFrequency, sprintFrequency, speedRatio);
            strideCycle += dt * freq;
            if (strideCycle >= 1f) strideCycle -= 1f;
        }

        // 3. Pounce timer
        if (isPouncing)
        {
            pounceTimer -= dt;
            if (pounceTimer <= 0f) isPouncing = false;
        }
    }

    private void LateUpdate()
    {
        if (hips == null) return;

        if (isKillMauling)
        {
            ApplyKillMaulPose();
            return;
        }

        // If stationary and not attacking, allow the base Animator idle/breathing clip to play naturally
        if (gaitWeight < 0.005f && !isPouncing) return;

        ApplyQuadrupedMotion();
    }

    private void ApplyQuadrupedMotion()
    {
        float twoPi = Mathf.PI * 2f;
        float t = strideCycle * twoPi;

        // Phase offsets for a predatory rotary gallop:
        // Front-Left leads -> Front-Right follows with 22% offset
        // Rear-Left kicks at 50% offset -> Rear-Right kicks with 72% offset
        float phaseFL = t;
        float phaseFR = t + 0.22f * twoPi;
        float phaseRL = t + 0.50f * twoPi;
        float phaseRR = t + 0.72f * twoPi;

        float weight = gaitWeight;

        // -------------------------------------------------------------
        // 1. Spine & Pelvis Dynamics (Dorsal arching and vertical heave)
        // -------------------------------------------------------------
        // When rear legs push, spine flexes upwards (pitch > 0), hips heave down.
        // During flight phase, spine extends flat, hips rise up.
        float heaveSine = Mathf.Sin(t * 2f); // 2 vertical bobs per full stride cycle
        Vector3 heaveOffset = new Vector3(0f, heaveSine * verticalHeave * weight, 0f);
        hips.localPosition = hipsBasePos + heaveOffset;

        float spineFlex = Mathf.Sin(t) * spinePitchFlex * weight;
        float bodyRoll = Mathf.Cos(t) * bodyRollSway * weight;

        // Hips pitch & roll
        hips.localRotation = hipsBaseRot * Quaternion.Euler(spineFlex * 0.45f, 0f, bodyRoll);

        // Mid-Spine & Chest arching
        if (spineMid != null)
            spineMid.localRotation = spineMidBaseRot * Quaternion.Euler(-spineFlex * 0.55f, 0f, -bodyRoll * 0.4f);

        if (chest != null)
            chest.localRotation = chestBaseRot * Quaternion.Euler(-spineFlex * 0.45f, 0f, -bodyRoll * 0.3f);

        if (neck != null)
            neck.localRotation = neckBaseRot;

        // -------------------------------------------------------------
        // 2. Forelimbs (Front Left & Front Right Paws)
        // -------------------------------------------------------------
        ApplyForelimb(frontArmL, frontForearmL, frontHandL,
                      frontArmLBaseRot, frontForearmLBaseRot, frontHandLBaseRot,
                      phaseFL, weight);

        ApplyForelimb(frontArmR, frontForearmR, frontHandR,
                      frontArmRBaseRot, frontForearmRBaseRot, frontHandRBaseRot,
                      phaseFR, weight);

        // -------------------------------------------------------------
        // 3. Hindlimbs (Rear Left & Rear Right Legs)
        // -------------------------------------------------------------
        ApplyHindlimb(rearLegL, rearCalfL, rearFootL,
                     rearLegLBaseRot, rearCalfLBaseRot, rearFootLBaseRot,
                     phaseRL, weight);

        ApplyHindlimb(rearLegR, rearCalfR, rearFootR,
                     rearLegRBaseRot, rearCalfRBaseRot, rearFootRBaseRot,
                     phaseRR, weight);

        // -------------------------------------------------------------
        // 4. Head & Snapping Jaws (World-Space Horizontal Tracking)
        // -------------------------------------------------------------
        if (head != null)
        {
            float headPitch = -Mathf.Sin(t) * headBobAngle * weight;
            head.localRotation = headBaseRot * Quaternion.Euler(headPitch, 0f, 0f);

            if (lookTarget != null)
            {
                Vector3 toTarget = (lookTarget.position + Vector3.up * 1.4f) - head.position;
                Vector3 flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z);
                Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z);

                if (flatToTarget.sqrMagnitude > 0.04f && flatForward.sqrMagnitude > 0.04f)
                {
                    float signedYaw = Vector3.SignedAngle(flatForward.normalized, flatToTarget.normalized, Vector3.up);
                    if (Mathf.Abs(signedYaw) < 95f)
                    {
                        float clampedYaw = Mathf.Clamp(signedYaw, -55f, 55f) * weight;
                        head.rotation = Quaternion.AngleAxis(clampedYaw, Vector3.up) * head.rotation;
                    }
                }
            }
        }

        if (jaw != null)
        {
            // Jaws snap open/shut rhythmically in attack gallop
            float jawSnap = Mathf.Max(0f, Mathf.Sin(t * 1.5f)) * jawSnapAngle * weight;
            jaw.localRotation = jawBaseRot * Quaternion.Euler(jawSnap, 0f, 0f);
        }

        // -------------------------------------------------------------
        // 5. Pounce Override (if currently attacking)
        // -------------------------------------------------------------
        if (isPouncing)
        {
            ApplyPouncePose();
        }
    }

    private void ApplyForelimb(Transform arm, Transform forearm, Transform hand,
                               Quaternion baseArm, Quaternion baseForearm, Quaternion baseHand,
                               float phase, float weight)
    {
        if (arm == null) return;

        // Shoulder swing: reaches forward, sweeps backward along X
        float armSwing = Mathf.Sin(phase) * frontArmPitchSwing * weight;
        arm.localRotation = baseArm * Quaternion.Euler(armSwing, 0f, 0f);

        // Elbow: flexes during recovery forward, extends during backward drive
        if (forearm != null)
        {
            float elbowFlex = (Mathf.Sin(phase - 0.7f) * 0.5f + 0.5f) * frontElbowFlex * weight;
            forearm.localRotation = baseForearm * Quaternion.Euler(-elbowFlex, 0f, 0f);
        }

        // Paw: maintains level angle against ground
        if (hand != null)
        {
            float pawPitch = -Mathf.Sin(phase) * frontPawPitch * weight;
            hand.localRotation = baseHand * Quaternion.Euler(pawPitch, 0f, 0f);
        }
    }

    private void ApplyHindlimb(Transform thigh, Transform calf, Transform foot,
                               Quaternion baseThigh, Quaternion baseCalf, Quaternion baseFoot,
                               float phase, float weight)
    {
        if (thigh == null) return;

        // Hip swing: drives backward forcefully, coils forward under body
        float thighSwing = Mathf.Sin(phase) * rearLegPitchSwing * weight;
        thigh.localRotation = baseThigh * Quaternion.Euler(thighSwing, 0f, 0f);

        // Knee / Shin: flexes as leg swings under belly, locks straight at push-off
        if (calf != null)
        {
            float kneeBend = (Mathf.Sin(phase + 0.4f) * 0.5f + 0.5f) * rearKneeFlex * weight;
            calf.localRotation = baseCalf * Quaternion.Euler(-kneeBend, 0f, 0f);
        }

        // Foot / Hock: flexes on recovery, springs back at launch
        if (foot != null)
        {
            float footPitch = -Mathf.Sin(phase) * rearFootPitch * weight;
            foot.localRotation = baseFoot * Quaternion.Euler(footPitch, 0f, 0f);
        }
    }

    private void ApplyPouncePose()
    {
        float pounceNorm = 1f - (pounceTimer / pounceDuration); // 0 to 1
        // Smooth leap curve (up and forward lunge, then land)
        float lungeCurve = Mathf.Sin(pounceNorm * Mathf.PI);

        if (hips != null)
        {
            hips.localPosition += new Vector3(0f, lungeCurve * 0.16f, lungeCurve * 0.14f);
            hips.localRotation *= Quaternion.Euler(-lungeCurve * 18f, 0f, 0f);
        }

        if (jaw != null)
        {
            // Wide snarling bite
            jaw.localRotation = jawBaseRot * Quaternion.Euler(lungeCurve * 48f, 0f, 0f);
        }

        if (frontArmL != null) frontArmL.localRotation *= Quaternion.Euler(lungeCurve * 32f, 0f, 0f);
        if (frontArmR != null) frontArmR.localRotation *= Quaternion.Euler(lungeCurve * 32f, 0f, 0f);
    }

    /// <summary>
    /// Procedurally animates the full terrifying jumpscare maul sequence.
    /// Operates in unscaled time so it continues during game over sequences.
    /// </summary>
    private void ApplyKillMaulPose()
    {
        killMaulTimer += Time.unscaledDeltaTime;
        float t = killMaulTimer;

        // Phase 1: 0.0s to 0.48s - Beast Rears Up on Hind Legs & Unhinges Jaws (Towering Monster)
        if (t < 0.48f)
        {
            float norm = Mathf.Clamp01(t / 0.48f);
            float ease = Mathf.SmoothStep(0f, 1f, norm);

            // Hind legs squat and brace
            if (rearLegL != null) rearLegL.localRotation = rearLegLBaseRot * Quaternion.Euler(ease * 32f, 0f, 0f);
            if (rearLegR != null) rearLegR.localRotation = rearLegRBaseRot * Quaternion.Euler(ease * 32f, 0f, 0f);
            if (rearCalfL != null) rearCalfL.localRotation = rearCalfLBaseRot * Quaternion.Euler(-ease * 40f, 0f, 0f);
            if (rearCalfR != null) rearCalfR.localRotation = rearCalfRBaseRot * Quaternion.Euler(-ease * 40f, 0f, 0f);

            // Pelvis & Spine heave UP and pitch back dramatically (beast rears up 1.5m tall)
            if (hips != null)
            {
                hips.localPosition = hipsBasePos + new Vector3(0f, ease * 0.28f, -ease * 0.12f);
                hips.localRotation = hipsBaseRot * Quaternion.Euler(-ease * 38f, 0f, 0f);
            }
            if (spineMid != null) spineMid.localRotation = spineMidBaseRot * Quaternion.Euler(-ease * 26f, 0f, 0f);
            if (chest != null) chest.localRotation = chestBaseRot * Quaternion.Euler(-ease * 20f, 0f, 0f);

            // Head pitches forward to glare straight into the player's eyes
            if (head != null)
            {
                float tremor = Mathf.Sin(t * 45f) * 4f;
                head.localRotation = headBaseRot * Quaternion.Euler(ease * 42f + tremor, 0f, 0f);
            }

            // Horrific wide snarling jaw snaps open
            if (jaw != null)
            {
                jaw.localRotation = jawBaseRot * Quaternion.Euler(ease * 55f, 0f, 0f);
            }

            // Forelimbs (massive arms) raise high to clamp onto player's face
            if (frontArmL != null) frontArmL.localRotation = frontArmLBaseRot * Quaternion.Euler(-ease * 42f, -ease * 22f, ease * 18f);
            if (frontArmR != null) frontArmR.localRotation = frontArmRBaseRot * Quaternion.Euler(-ease * 42f, ease * 22f, -ease * 18f);
            if (frontForearmL != null) frontForearmL.localRotation = frontForearmLBaseRot * Quaternion.Euler(-ease * 30f, 0f, 0f);
            if (frontForearmR != null) frontForearmR.localRotation = frontForearmRBaseRot * Quaternion.Euler(-ease * 30f, 0f, 0f);
        }
        // Phase 2: 0.48s to 1.35s - Savage Forward Leap & Clamping Maul (Violent Jumpscare Strike)
        else if (t < 1.35f)
        {
            float maulT = t - 0.48f;
            float lunge = Mathf.Clamp01(maulT / 0.35f);

            // Spine lunges directly forward into camera lens
            if (hips != null)
            {
                hips.localPosition = hipsBasePos + new Vector3(0f, 0.18f, lunge * 0.42f);
                hips.localRotation = hipsBaseRot * Quaternion.Euler(-18f + Mathf.Sin(maulT * 24f) * 6f, 0f, 0f);
            }
            if (spineMid != null) spineMid.localRotation = spineMidBaseRot * Quaternion.Euler(-12f, 0f, 0f);
            if (chest != null) chest.localRotation = chestBaseRot * Quaternion.Euler(-8f, 0f, 0f);

            // Jaws snap violently shut and open in ferocious mauling bites (3-4 vicious snaps)
            float snapCycle = Mathf.Abs(Mathf.Sin(maulT * 18f));
            if (jaw != null)
            {
                float jawAngle = Mathf.Lerp(12f, 58f, snapCycle);
                jaw.localRotation = jawBaseRot * Quaternion.Euler(jawAngle, 0f, 0f);
            }

            // Head shakes violently during the bite (predator tearing motion)
            if (head != null)
            {
                float headShakeYaw = Mathf.Sin(maulT * 32f) * 14f;
                float headShakePitch = Mathf.Cos(maulT * 26f) * 10f;
                head.localRotation = headBaseRot * Quaternion.Euler(headShakePitch, headShakeYaw, 0f);
            }

            // Claws swipe and clamp forward
            if (frontArmL != null) frontArmL.localRotation = frontArmLBaseRot * Quaternion.Euler(22f, 28f, 12f);
            if (frontArmR != null) frontArmR.localRotation = frontArmRBaseRot * Quaternion.Euler(22f, -28f, -12f);
            if (frontHandL != null) frontHandL.localRotation = frontHandLBaseRot * Quaternion.Euler(30f, 0f, 0f);
            if (frontHandR != null) frontHandR.localRotation = frontHandRBaseRot * Quaternion.Euler(30f, 0f, 0f);
        }
        // Phase 3: 1.35s to 3.5s - Monster Towers Over Fallen Player Pinning to Floor
        else
        {
            float groundT = t - 1.35f;

            // Monster towers directly over fallen player
            if (hips != null)
            {
                hips.localPosition = hipsBasePos + new Vector3(0f, -0.05f, 0.45f);
                hips.localRotation = hipsBaseRot * Quaternion.Euler(14f, 0f, 0f);
            }

            // Head looks straight DOWN into fallen camera with snarling breathing
            if (head != null)
            {
                float breathNod = Mathf.Sin(groundT * 7f) * 6f;
                head.localRotation = headBaseRot * Quaternion.Euler(48f + breathNod, 0f, 0f);
            }

            // Heavy panting and snapping jaws inches from the lens
            if (jaw != null)
            {
                float snarl = Mathf.Max(0f, Mathf.Sin(groundT * 9f)) * 36f + 16f;
                jaw.localRotation = jawBaseRot * Quaternion.Euler(snarl, 0f, 0f);
            }

            // Front limbs planted firmly on the floor around the camera
            if (frontArmL != null) frontArmL.localRotation = frontArmLBaseRot * Quaternion.Euler(38f, -15f, 0f);
            if (frontArmR != null) frontArmR.localRotation = frontArmRBaseRot * Quaternion.Euler(38f, 15f, 0f);
        }
    }

    /// <summary>
    /// Triggers a vicious pounce/lunge bite attack.
    /// </summary>
    public void TriggerPounce()
    {
        isPouncing = true;
        pounceTimer = pounceDuration;
    }
}
