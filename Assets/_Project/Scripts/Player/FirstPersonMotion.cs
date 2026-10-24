using UnityEngine;

namespace StarterAssets
{
    /// <summary>
    /// Implements realistic physical first-person camera movement for the player:
    /// 1. Idle Respiration / Breathing with post-sprint exertion dynamics.
    /// 2. Bipedal Gait Head Bobbing (Harmonic Lissajous figure-8) synchronized with speed.
    /// 3. Step impacts & visceral fall landing recoil with physical spring recovery.
    /// 4. Strafe banking & look-turn rotational inertia (handheld camera feel).
    /// 5. Synchronized footstep audio playback loaded from Resources/SFX/Footsteps.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(FirstPersonController))]
    public class FirstPersonMotion : MonoBehaviour
    {
        [Header("Idle Respiration / Breathing")]
        [Tooltip("Enable resting chest breathing & sprint exertion.")]
        public bool enableBreathing = true;
        [Tooltip("Resting breathing frequency in Hz (~14.4 breaths/min).")]
        public float restingBreathFrequency = 0.24f;
        [Tooltip("Exertion breathing frequency in Hz (~39 breaths/min after sprinting).")]
        public float exertionBreathFrequency = 0.65f;
        [Tooltip("Vertical chest rise and fall amplitude in meters.")]
        public float restingBreathAmpY = 0.006f;
        [Tooltip("Exertion multiplier for breathing amplitude.")]
        public float exertionAmpMultiplier = 2.4f;
        [Tooltip("Pitch nod amplitude in degrees during respiration.")]
        public float breathPitchAmp = 0.28f;
        [Tooltip("Subtle roll tilt amplitude during respiration.")]
        public float breathRollAmp = 0.16f;
        [Tooltip("Subtle organic handheld camera drift amplitude in degrees.")]
        public float handheldDriftAmount = 0.18f;
        public float handheldDriftSpeed = 0.5f;

        [Header("Gait / Head Bobbing (Harmonic Lissajous)")]
        [Tooltip("Enable gait head bobbing while walking and sprinting.")]
        public bool enableHeadBob = true;
        [Tooltip("Stride cycle frequency in Hz when walking (~1.9 steps/sec).")]
        public float walkBobFrequency = 0.95f;
        [Tooltip("Stride cycle frequency in Hz when sprinting (~2.7 steps/sec).")]
        public float sprintBobFrequency = 1.35f;
        [Tooltip("Walking bob amplitude (X: lateral sway, Y: vertical dip, Z: forward-back).")]
        public Vector3 walkBobAmplitude = new Vector3(0.015f, 0.022f, 0.009f);
        [Tooltip("Sprinting bob amplitude.")]
        public Vector3 sprintBobAmplitude = new Vector3(0.030f, 0.046f, 0.018f);
        [Tooltip("Walking camera roll tilt in degrees per step.")]
        public float walkRollAngle = 0.85f;
        [Tooltip("Sprinting camera roll tilt in degrees per step.")]
        public float sprintRollAngle = 1.90f;
        [Tooltip("Walking pitch nod in degrees.")]
        public float walkPitchAngle = 0.40f;
        [Tooltip("Sprinting pitch nod in degrees.")]
        public float sprintPitchAngle = 1.05f;

        [Header("Dynamic Lean & Inertia")]
        [Tooltip("Enable strafe roll banking and rotational inertia.")]
        public bool enableLeanAndInertia = true;
        [Tooltip("Camera roll tilt when strafing left/right in degrees.")]
        public float strafeLeanAngle = 1.50f;
        [Tooltip("Camera roll tilt when panning the view left/right in degrees.")]
        public float turnInertiaAngle = 1.25f;
        [Tooltip("Camera pitch nod when accelerating forward or stopping in degrees.")]
        public float accelPitchAngle = 0.75f;
        [Tooltip("Smoothing speed for lean and inertia angles.")]
        public float leanDamping = 8.0f;

        [Header("Impacts & Landing")]
        [Tooltip("Enable physical footstep compression and landing dip.")]
        public bool enableImpacts = true;
        [Tooltip("Spring stiffness for recovering from impacts.")]
        public float springStiffness = 32.0f;
        [Tooltip("Spring damping for recovering from impacts.")]
        public float springDamping = 14.0f;
        [Tooltip("Landing compression factor per unit vertical fall speed.")]
        public float landingDipMultiplier = 0.024f;
        [Tooltip("Maximum downward landing compression in meters.")]
        public float landingMaxDip = 0.11f;

        [Header("Footstep Audio")]
        [Tooltip("Enable footsteps audio synchronized with foot plants.")]
        public bool enableAudio = true;
        [Range(0f, 1f)] public float walkVolume = 0.35f;
        [Range(0f, 1f)] public float sprintVolume = 0.70f;
        [Range(0f, 1f)] public float landVolume = 0.80f;

        // References
        private FirstPersonController _fpc;
        private CharacterController _character;
        private Transform _cameraRoot;
        private AudioSource _audioSource;
        private AudioClip[] _footstepClips;
        private AudioClip _landClip;

        // Internal State
        private Vector3 _baseRootLocalPosition;
        private float _breathPhase;
        private float _gaitPhase;
        private float _exertion; // 0 (calm) to 1 (heavy exertion)
        private float _currentBobWeight;
        private float _currentStrafeRoll;
        private float _currentTurnRoll;
        private float _currentAccelPitch;
        private float _lastSpeed;
        private float _landingOffset;
        private float _landingVelocity;
        private bool _wasGrounded = true;
        private float _lastVerticalVelocity;
        private bool _stepTriggerArmed;
        private float _lastStepTime;

        private void Awake()
        {
            _fpc = GetComponent<FirstPersonController>();
            _character = GetComponent<CharacterController>();

            if (_fpc != null && _fpc.CinemachineCameraTarget != null)
            {
                _cameraRoot = _fpc.CinemachineCameraTarget.transform;
                _baseRootLocalPosition = _cameraRoot.localPosition;
            }
            else
            {
                // Fallback search
                Transform root = transform.Find("PlayerCameraRoot");
                if (root != null)
                {
                    _cameraRoot = root;
                    _baseRootLocalPosition = _cameraRoot.localPosition;
                }
            }

            SetupAudio();
        }

        private void SetupAudio()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0.0f; // 2D player audio
            _audioSource.volume = walkVolume;

            // Load clips from Resources
            _footstepClips = Resources.LoadAll<AudioClip>("SFX/Footsteps");
            _landClip = Resources.Load<AudioClip>("SFX/Footsteps/Player_Land");

            // Filter out Player_Land from footstep list if present
            if (_footstepClips != null && _footstepClips.Length > 0)
            {
                var list = new System.Collections.Generic.List<AudioClip>();
                foreach (var clip in _footstepClips)
                {
                    if (clip != null && clip.name.StartsWith("Player_Footstep"))
                    {
                        list.Add(clip);
                    }
                }
                if (list.Count > 0) _footstepClips = list.ToArray();
            }
        }

        private void Update()
        {
            if (GameOverController.IsGameOver) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Track ground landing transitions
            bool isGrounded = _character != null ? _character.isGrounded : true;
            if (!_wasGrounded && isGrounded && enableImpacts)
            {
                float fallSpeed = Mathf.Abs(_lastVerticalVelocity);
                if (fallSpeed > 2.2f)
                {
                    float dip = Mathf.Clamp(fallSpeed * landingDipMultiplier, 0.02f, landingMaxDip);
                    _landingVelocity -= dip * 25.0f;

                    // Play landing sound
                    if (enableAudio && _landClip != null && _audioSource != null)
                    {
                        _audioSource.pitch = Random.Range(0.95f, 1.05f);
                        _audioSource.PlayOneShot(_landClip, landVolume);
                    }
                }
            }

            _wasGrounded = isGrounded;
            if (_fpc != null)
            {
                _lastVerticalVelocity = _fpc.VerticalVelocity;
            }

            // Spring simulation for landing impact
            if (enableImpacts)
            {
                float springForce = -_landingOffset * springStiffness - _landingVelocity * springDamping;
                _landingVelocity += springForce * dt;
                _landingOffset += _landingVelocity * dt;
                if (Mathf.Abs(_landingOffset) < 0.0005f && Mathf.Abs(_landingVelocity) < 0.001f)
                {
                    _landingOffset = 0f;
                    _landingVelocity = 0f;
                }
            }

            // Update sprint exertion
            bool isSprinting = _fpc != null && _fpc.IsSprinting && _fpc.CurrentSpeed > 3.0f;
            float targetExertion = isSprinting ? 1.0f : 0.0f;
            if (_fpc != null && _fpc.Stamina != null)
            {
                targetExertion = Mathf.Max(targetExertion, (1.0f - _fpc.Stamina.NormalizedStamina) * 0.85f);
            }
            float exertionRate = isSprinting ? 0.30f : 0.18f;
            _exertion = Mathf.MoveTowards(_exertion, targetExertion, dt * exertionRate);
        }

        private void LateUpdate()
        {
            if (GameOverController.IsGameOver || _cameraRoot == null || _fpc == null || PlayerVentController.IsInVent) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 finalPosOffset = Vector3.zero;
            float pitchOffset = 0f;
            float yawOffset = 0f;
            float rollOffset = 0f;

            float horizontalSpeed = 0f;
            if (_character != null)
            {
                Vector3 vel = _character.velocity;
                horizontalSpeed = new Vector2(vel.x, vel.z).magnitude;
            }

            bool isGrounded = _character != null ? _character.isGrounded : true;
            bool isMoving = isGrounded && horizontalSpeed > 0.15f;
            bool isSprinting = isMoving && _fpc.IsSprinting && horizontalSpeed > 4.5f;

            // ----------------------------------------------------
            // 1. Idle Respiration / Breathing
            // ----------------------------------------------------
            if (enableBreathing)
            {
                float breathFreq = Mathf.Lerp(restingBreathFrequency, exertionBreathFrequency, _exertion);
                _breathPhase += (breathFreq * Mathf.PI * 2f) * dt;
                if (_breathPhase > Mathf.PI * 2f) _breathPhase -= Mathf.PI * 2f;

                float ampMul = Mathf.Lerp(1.0f, exertionAmpMultiplier, _exertion);
                // Lower breathing amplitude slightly while running so gait bob dominates
                float breathMoveDamp = isMoving ? 0.35f : 1.0f;

                float breathSin = Mathf.Sin(_breathPhase);
                float breathCos = Mathf.Cos(_breathPhase);

                finalPosOffset.y += breathSin * (restingBreathAmpY * ampMul * breathMoveDamp);
                finalPosOffset.z += breathCos * (restingBreathAmpY * 0.4f * ampMul * breathMoveDamp);

                pitchOffset += breathSin * (breathPitchAmp * ampMul * breathMoveDamp);
                rollOffset += Mathf.Cos(_breathPhase * 0.5f) * (breathRollAmp * ampMul * breathMoveDamp);

                // Handheld organic drift (subtle perlin noise)
                float tDrift = Time.time * handheldDriftSpeed;
                float driftX = (Mathf.PerlinNoise(tDrift, 0.0f) - 0.5f) * 2f;
                float driftY = (Mathf.PerlinNoise(0.0f, tDrift) - 0.5f) * 2f;
                yawOffset += driftX * handheldDriftAmount;
                pitchOffset += driftY * (handheldDriftAmount * 0.6f);
            }

            // ----------------------------------------------------
            // 2. Gait / Head Bobbing (Harmonic Lissajous)
            // ----------------------------------------------------
            float targetBobWeight = isMoving ? 1.0f : 0.0f;
            _currentBobWeight = Mathf.MoveTowards(_currentBobWeight, targetBobWeight, dt * 6.0f);

            if (enableHeadBob && _currentBobWeight > 0.001f)
            {
                float targetStrideFreq = isSprinting ? sprintBobFrequency : walkBobFrequency;
                float refSpeed = isSprinting ? 6.0f : 4.0f;
                float speedFactor = Mathf.Clamp(horizontalSpeed / refSpeed, 0.35f, 1.25f);

                _gaitPhase += (targetStrideFreq * Mathf.PI * 2f) * speedFactor * dt;
                if (_gaitPhase > Mathf.PI * 2f) _gaitPhase -= Mathf.PI * 2f;

                Vector3 currentAmp = Vector3.Lerp(walkBobAmplitude, sprintBobAmplitude, isSprinting ? 1.0f : 0.0f) * _currentBobWeight;
                float currentRoll = Mathf.Lerp(walkRollAngle, sprintRollAngle, isSprinting ? 1.0f : 0.0f) * _currentBobWeight;
                float currentPitch = Mathf.Lerp(walkPitchAngle, sprintPitchAngle, isSprinting ? 1.0f : 0.0f) * _currentBobWeight;

                // Lissajous Figure-8 Gait:
                // X: lateral sway once per stride (sin(gaitPhase))
                // Y: vertical dip twice per stride (-abs(sin) or (sin(2*gaitPhase - pi/2) * 0.5 - 0.5))
                // Z: forward push
                float swayX = Mathf.Sin(_gaitPhase);
                float dipY = Mathf.Sin(_gaitPhase * 2.0f - Mathf.PI * 0.5f) * 0.5f - 0.5f; // strictly <= 0
                float thrustZ = Mathf.Cos(_gaitPhase * 2.0f);

                finalPosOffset.x += swayX * currentAmp.x;
                finalPosOffset.y += dipY * currentAmp.y;
                finalPosOffset.z += thrustZ * currentAmp.z;

                rollOffset += swayX * currentRoll;
                pitchOffset += Mathf.Sin(_gaitPhase * 2.0f) * currentPitch;

                // Footstep event detection at wave trough
                // dipY reaches bottom (-1.0) when 2*gaitPhase - pi/2 = 3*pi/2 => gaitPhase = pi or 0
                if (dipY < -0.85f && !_stepTriggerArmed)
                {
                    _stepTriggerArmed = true;
                    OnFootstepLanded(isSprinting);
                }
                else if (dipY > -0.40f)
                {
                    _stepTriggerArmed = false;
                }
            }
            else
            {
                _stepTriggerArmed = false;
            }

            // ----------------------------------------------------
            // 3. Dynamic Lean & Rotational Inertia
            // ----------------------------------------------------
            if (enableLeanAndInertia)
            {
                // Strafe lean (left/right input)
                float moveX = (_fpc.Input != null) ? _fpc.Input.move.x : 0f;
                float targetStrafe = -moveX * strafeLeanAngle;
                _currentStrafeRoll = Mathf.Lerp(_currentStrafeRoll, targetStrafe, dt * leanDamping);
                rollOffset += _currentStrafeRoll;

                // Turn banking (yaw look velocity inertia)
                float turnVel = _fpc.RotationVelocity;
                float targetTurn = -Mathf.Clamp(turnVel * 0.15f, -1.0f, 1.0f) * turnInertiaAngle;
                _currentTurnRoll = Mathf.Lerp(_currentTurnRoll, targetTurn, dt * leanDamping);
                rollOffset += _currentTurnRoll;

                // Acceleration / Deceleration pitch
                float accel = (horizontalSpeed - _lastSpeed) / Mathf.Max(dt, 0.001f);
                float targetAccelPitch = Mathf.Clamp(accel * -0.06f, -accelPitchAngle, accelPitchAngle);
                _currentAccelPitch = Mathf.Lerp(_currentAccelPitch, targetAccelPitch, dt * leanDamping);
                pitchOffset += _currentAccelPitch;
            }

            _lastSpeed = horizontalSpeed;

            // ----------------------------------------------------
            // 4. Landing Impact Offset
            // ----------------------------------------------------
            finalPosOffset.y += _landingOffset;

            // ----------------------------------------------------
            // 5. Apply Final Motion to Camera Root
            // ----------------------------------------------------
            _cameraRoot.localPosition = _baseRootLocalPosition + finalPosOffset;

            float basePitch = _fpc.CinemachineTargetPitch;
            _cameraRoot.localRotation = Quaternion.Euler(basePitch + pitchOffset, yawOffset, rollOffset);
        }

        private void OnFootstepLanded(bool isSprinting)
        {
            // Enforce minimum time between steps to prevent spamming
            float minInterval = isSprinting ? 0.30f : 0.44f;
            if (Time.time - _lastStepTime < minInterval) return;
            _lastStepTime = Time.time;

            // Subtle micro-compression jolt on foot impact
            if (enableImpacts)
            {
                _landingVelocity -= 0.08f;
            }

            // Play random footstep audio
            if (enableAudio && _footstepClips != null && _footstepClips.Length > 0 && _audioSource != null)
            {
                AudioClip clip = _footstepClips[Random.Range(0, _footstepClips.Length)];
                if (clip != null)
                {
                    _audioSource.pitch = Random.Range(0.92f, 1.08f);
                    float vol = isSprinting ? sprintVolume : walkVolume;
                    _audioSource.PlayOneShot(clip, vol);
                }
            }
        }
    }
}
