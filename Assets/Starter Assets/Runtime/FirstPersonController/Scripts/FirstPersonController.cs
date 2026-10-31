using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		private float _speed;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

	
#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;
		private PlayerStamina _stamina;
		private bool _isSprinting;

		private const float _threshold = 0.01f;

		// Backrooms: look sensitivity chosen in the menu (PlayerPrefs key written by GameSettings)
		private float _sensitivity = 1f;

		public float CinemachineTargetPitch => _cinemachineTargetPitch;
		public float RotationVelocity => _rotationVelocity;
		public StarterAssetsInputs Input => _input;
		public CharacterController Controller => _controller;
		public float CurrentSpeed => _speed;
		public float VerticalVelocity => _verticalVelocity;
		public bool IsSprinting => _isSprinting;
		public PlayerStamina Stamina => _stamina;

		// Backrooms: exposed so other systems (e.g. vent hiding) can take over the camera and hand it back cleanly
		public float LookSensitivity => _sensitivity;
		public bool UsesDirectLookDelta => IsCurrentDeviceMouse || TouchscreenInput.IsTouchLookActive;
		public float CurrentPitch => _cinemachineTargetPitch;

		/// <summary>Overwrites the stored camera pitch (and applies it) so control resumes without a snap.</summary>
		public void SetPitch(float pitch)
		{
			_cinemachineTargetPitch = ClampAngle(pitch, BottomClamp, TopClamp);
			if (CinemachineCameraTarget != null)
				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);
		}

		/// <summary>Clears leftover velocity / jump state, used when control is returned after a scripted sequence.</summary>
		public void ResetMotionState()
		{
			_speed = 0f;
			_verticalVelocity = -2f;
			_rotationVelocity = 0f;
			_jumpTimeoutDelta = JumpTimeout;
			if (_input != null) _input.jump = false;
		}

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		private void Awake()
		{
			_sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat("backrooms.sensitivity", 1f), 0.1f, 2f);

			_stamina = GetComponent<PlayerStamina>();
			if (_stamina == null)
			{
				_stamina = gameObject.AddComponent<PlayerStamina>();
			}

			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void OnEnable()
		{
			TouchscreenInput.OnSensitivityMultiplierChanged += HandleSensitivityChanged;
		}

		private void OnDisable()
		{
			TouchscreenInput.OnSensitivityMultiplierChanged -= HandleSensitivityChanged;
		}

		private void HandleSensitivityChanged(float val)
		{
			_sensitivity = Mathf.Clamp(val, 0.1f, 2f);
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

			// reset our timeouts on start
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;
		}

		private void Update()
		{
			JumpAndGravity();
			GroundedCheck();
			Move();
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck()
		{
			if (_controller != null && _controller.isGrounded)
			{
				Grounded = true;
				return;
			}

			// Fallback sphere check when leaving ledges or on slopes
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void CameraRotation()
		{
			// if there is an input
			if (_input.look.sqrMagnitude >= _threshold)
			{
				//Don't multiply mouse input by Time.deltaTime
				bool isDirectDelta = IsCurrentDeviceMouse || TouchscreenInput.IsTouchLookActive;
				float deltaTimeMultiplier = isDirectDelta ? 1.0f : Time.deltaTime;
				
				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier * _sensitivity;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier * _sensitivity;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// Update Cinemachine camera target pitch
				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}
			else
			{
				_rotationVelocity = 0.0f;
			}
		}

		private void Move()
		{
			bool isMoving = _input.move != Vector2.zero;
			bool wantsToSprint = _input.sprint && isMoving && Grounded;
			bool canSprint = _stamina == null || _stamina.CanSprint;
			_isSprinting = wantsToSprint && canSprint;

			if (_stamina != null)
			{
				_stamina.UpdateMovement(_isSprinting, isMoving, Time.deltaTime);
			}

			// set target speed based on move speed, sprint speed and whether sprint is actively maintained
			float targetSpeed = _isSprinting ? SprintSpeed : MoveSpeed;
			if (!isMoving) targetSpeed = 0.0f;

			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			// Accelerate smoothly and responsively towards target speed
			_speed = Mathf.MoveTowards(_speed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
			_speed = Mathf.Round(_speed * 1000f) / 1000f;

			// normalise input direction
			Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

			if (_input.move != Vector2.zero)
			{
				inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
			}

			// Smooth slope projection: prevents jittering, snagging, or speed drops when running up/down rolling hills
			if (Grounded && Physics.Raycast(transform.position + Vector3.up * 0.25f, Vector3.down, out RaycastHit slopeHit, 0.85f, GroundLayers, QueryTriggerInteraction.Ignore))
			{
				inputDirection = Vector3.ProjectOnPlane(inputDirection, slopeHit.normal);
			}

			// move the player
			if (_controller != null && _controller.enabled)
			{
				CollisionFlags flags = _controller.Move(inputDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

				// Head-bump / Ceiling hit detection: cancel upward velocity immediately so player drops naturally
				if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0.0f)
				{
					_verticalVelocity = -0.5f;
				}
			}

			// Dynamic Sprint FOV
			UpdateSprintFov();
		}

		private Component _cachedCinemachineCam;
		private System.Reflection.PropertyInfo _lensProperty;
		private System.Reflection.FieldInfo _fovField;
		private float _baseFov = 65f;
		private bool _fovInitialized = false;

		private void UpdateSprintFov()
		{
			if (!_fovInitialized)
			{
				var pfc = GameObject.Find("PlayerFollowCamera");
				if (pfc != null)
				{
					foreach (var comp in pfc.GetComponents<Component>())
					{
						if (comp != null && comp.GetType().Name == "CinemachineCamera")
						{
							_cachedCinemachineCam = comp;
							_lensProperty = comp.GetType().GetProperty("Lens");
							if (_lensProperty != null)
							{
								var lensVal = _lensProperty.GetValue(comp);
								if (lensVal != null)
								{
									var fovField = lensVal.GetType().GetField("FieldOfView");
									if (fovField != null)
									{
										_fovField = fovField;
										_baseFov = (float)fovField.GetValue(lensVal);
										_fovInitialized = true;
									}
								}
							}
							break;
						}
					}
				}
				if (!_fovInitialized) _fovInitialized = true;
			}

			if (_cachedCinemachineCam != null && _lensProperty != null && _fovField != null)
			{
				var lensVal = _lensProperty.GetValue(_cachedCinemachineCam);
				if (lensVal != null)
				{
					float currentFov = (float)_fovField.GetValue(lensVal);
					float targetFov = _isSprinting && _speed > MoveSpeed + 0.5f ? _baseFov + 10f : _baseFov;
					float newFov = Mathf.Lerp(currentFov, targetFov, Time.deltaTime * 6.5f);
					_fovField.SetValue(lensVal, newFov);
					_lensProperty.SetValue(_cachedCinemachineCam, lensVal);
				}
			}
		}

		private void JumpAndGravity()
		{
			// If controller is disabled (e.g. scripted sequence, sitting on couch, hiding in locker), keep velocity neutral
			if (_controller != null && !_controller.enabled)
			{
				_verticalVelocity = -2f;
				_fallTimeoutDelta = FallTimeout;
				_jumpTimeoutDelta = JumpTimeout;
				return;
			}

			if (Grounded)
			{
				// reset the fall timeout timer
				_fallTimeoutDelta = FallTimeout;

				// stop our velocity dropping infinitely when grounded
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				// Jump
				if (_input.jump && _jumpTimeoutDelta <= 0.0f)
				{
					// Check ceiling clearance above head before launching a full jump (prevents jumping through ceiling from high furniture)
					float headTop = transform.position.y + _controller.center.y + (_controller.height * 0.5f);
					Vector3 headOrigin = new Vector3(transform.position.x, headTop - 0.15f, transform.position.z);
					bool ceilingBlocked = Physics.SphereCast(headOrigin, _controller.radius * 0.75f, Vector3.up, out _, 0.35f, GroundLayers, QueryTriggerInteraction.Ignore);

					if (!ceilingBlocked)
					{
						// the square root of H * -2 * G = how much velocity needed to reach desired height
						_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
					}
					else
					{
						// Already touching or too close to ceiling: small subtle bounce
						_verticalVelocity = 0.5f;
					}
				}

				// jump timeout
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}
			else
			{
				// reset the jump timeout timer
				_jumpTimeoutDelta = JumpTimeout;

				// fall timeout
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				// if we are not grounded, do not jump
				_input.jump = false;
			}

			// apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}