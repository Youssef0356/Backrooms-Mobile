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
    public float walkFrequency = 1.35f;
    [Tooltip("Stride cycle frequency at full sprint/chase speed (cycles/sec).")]
    public float sprintFrequency = 2.45f;

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

    // Bone Transforms
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
        // 4. Head & Snapping Jaws
        // -------------------------------------------------------------
        if (head != null)
        {
            // Head counter-bobs to keep gaze fixed forward on prey
            float headPitch = -Mathf.Sin(t) * headBobAngle * weight;
            head.localRotation = headBaseRot * Quaternion.Euler(headPitch, 0f, 0f);
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
            hips.localPosition += new Vector3(0f, lungeCurve * 0.08f, lungeCurve * 0.06f);
            hips.localRotation *= Quaternion.Euler(-lungeCurve * 15f, 0f, 0f);
        }

        if (jaw != null)
        {
            // Wide snarling bite
            jaw.localRotation = jawBaseRot * Quaternion.Euler(lungeCurve * 38f, 0f, 0f);
        }

        if (frontArmL != null) frontArmL.localRotation *= Quaternion.Euler(lungeCurve * 25f, 0f, 0f);
        if (frontArmR != null) frontArmR.localRotation *= Quaternion.Euler(lungeCurve * 25f, 0f, 0f);
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
