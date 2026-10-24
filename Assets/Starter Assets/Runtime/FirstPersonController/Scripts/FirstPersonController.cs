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

			// a reference to the players current horizontal velocity
			float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			// accelerate or decelerate to target speed with responsive linear rate
			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				_speed = Mathf.MoveTowards(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate * 3.5f);
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed * inputMagnitude;
			}

			// normalise input direction
			Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

			// note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is a move input rotate player when the player is moving
			if (_input.move != Vector2.zero)
			{
				// move
				inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
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
		}

		private void JumpAndGravity()
		{
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