using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StarterAssets
{
    public class TouchscreenInput : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Move joystick magnitude is in [-1;1] range, this multiply it before sending it to move event")]
        public float MoveMagnitudeMultiplier = 1.0f;
        [Tooltip("Look magnitude multiplier")]
        public float LookMagnitudeMultiplier = 1.0f;
        [Tooltip("Sensitivity multiplier for touch swipe look on the right half of the screen")]
        [Range(0.1f, 3.0f)]
        public float TouchSensitivity = 1.0f;
        public bool InvertLookY;

        [Header("Events")]
        public UnityEvent<Vector2> MoveEvent;
        public UnityEvent<Vector2> LookEvent;
        public UnityEvent<bool> JumpEvent;
        public UnityEvent<bool> SprintEvent;

        // Flashlight cross-assembly bridge
        public static event Action OnFlashlightToggleRequested;
        public static bool IsFlashlightOn { get; private set; } = true;
        public static event Action<bool> OnFlashlightStateChanged;

        // Touch swipe look active flag (queried by FirstPersonController / ThirdPersonController)
        public static bool IsTouchLookActive { get; private set; }

        public static void SetFlashlightState(bool on)
        {
            IsFlashlightOn = on;
            OnFlashlightStateChanged?.Invoke(on);
        }

        private UIDocument m_Document;
        private VirtualJoystick m_MoveJoystick;
        private VirtualJoystick m_LookJoystick;

        private int m_ActiveLookPointerId = -1;
        private Vector2 m_LastLookPointerPos;
        private Vector2 m_AccumulatedLookDelta;
        private bool m_HasLookDelta;

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();

            var safeArea = Screen.safeArea;
            var root = m_Document.rootVisualElement;

            root.style.position = Position.Absolute;
            root.style.left = safeArea.xMin;
            root.style.right = Screen.width - safeArea.xMax;
            root.style.top = Screen.height - safeArea.yMax;
            root.style.bottom = safeArea.yMin;
        }

        private void Start()
        {
            var root = m_Document.rootVisualElement;

            var joystickMove = root.Q<VisualElement>("JoystickMove");
            if (joystickMove != null)
            {
                m_MoveJoystick = new VirtualJoystick(joystickMove);
                m_MoveJoystick.JoystickEvent.AddListener(mov =>
                {
                    MoveEvent?.Invoke(mov * MoveMagnitudeMultiplier);
                });
            }

            var touchLookZone = root.Q<VisualElement>("TouchLookZone");
            if (touchLookZone != null)
            {
                SetupTouchLookZone(touchLookZone);
            }
            else
            {
                var joystickLook = root.Q<VisualElement>("JoystickLook");
                if (joystickLook != null)
                {
                    m_LookJoystick = new VirtualJoystick(joystickLook);
                    m_LookJoystick.JoystickEvent.AddListener(mov =>
                    {
                        if (InvertLookY)
                            mov.y *= -1;

                        LookEvent?.Invoke(mov * LookMagnitudeMultiplier);
                    });
                }
            }

            var jumpButton = root.Q<VisualElement>("ButtonJump");
            if (jumpButton != null)
            {
                jumpButton.RegisterCallback<PointerEnterEvent>(evt => { JumpEvent?.Invoke(true); });
                jumpButton.RegisterCallback<PointerLeaveEvent>(evt => { JumpEvent?.Invoke(false); });
                jumpButton.RegisterCallback<PointerDownEvent>(evt => { JumpEvent?.Invoke(true); });
                jumpButton.RegisterCallback<PointerUpEvent>(evt => { JumpEvent?.Invoke(false); });
            }

            var sprintButton = root.Q<VisualElement>("ButtonSprint");
            if (sprintButton != null)
            {
                sprintButton.RegisterCallback<PointerEnterEvent>(evt => { SprintEvent?.Invoke(true); });
                sprintButton.RegisterCallback<PointerLeaveEvent>(evt => { SprintEvent?.Invoke(false); });
                sprintButton.RegisterCallback<PointerDownEvent>(evt => { SprintEvent?.Invoke(true); });
                sprintButton.RegisterCallback<PointerUpEvent>(evt => { SprintEvent?.Invoke(false); });
            }

            var flashlightButton = root.Q<VisualElement>("ButtonFlashlight");
            if (flashlightButton != null)
            {
                SetupFlashlightButton(flashlightButton);
            }
        }

        private void OnEnable()
        {
            OnFlashlightStateChanged += UpdateFlashlightVisual;
            UpdateFlashlightVisual(IsFlashlightOn);
        }

        private void OnDisable()
        {
            OnFlashlightStateChanged -= UpdateFlashlightVisual;
            ResetLookTouch();
        }

        private VisualElement m_FlashlightButton;

        private void SetupFlashlightButton(VisualElement btn)
        {
            m_FlashlightButton = btn;
            btn.RegisterCallback<PointerDownEvent>(evt =>
            {
                OnFlashlightToggleRequested?.Invoke();
            });
            UpdateFlashlightVisual(IsFlashlightOn);
        }

        private void UpdateFlashlightVisual(bool isOn)
        {
            if (m_FlashlightButton == null) return;

            if (isOn)
            {
                m_FlashlightButton.style.backgroundColor = new StyleColor(new Color(0.95f, 0.90f, 0.50f, 0.28f));
                m_FlashlightButton.style.borderTopColor = new StyleColor(new Color(0.95f, 0.90f, 0.50f, 0.8f));
                m_FlashlightButton.style.borderRightColor = new StyleColor(new Color(0.95f, 0.90f, 0.50f, 0.8f));
                m_FlashlightButton.style.borderBottomColor = new StyleColor(new Color(0.95f, 0.90f, 0.50f, 0.8f));
                m_FlashlightButton.style.borderLeftColor = new StyleColor(new Color(0.95f, 0.90f, 0.50f, 0.8f));
                m_FlashlightButton.style.borderTopWidth = 2f;
                m_FlashlightButton.style.borderRightWidth = 2f;
                m_FlashlightButton.style.borderBottomWidth = 2f;
                m_FlashlightButton.style.borderLeftWidth = 2f;
            }
            else
            {
                m_FlashlightButton.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0f));
                m_FlashlightButton.style.borderTopWidth = 0f;
                m_FlashlightButton.style.borderRightWidth = 0f;
                m_FlashlightButton.style.borderBottomWidth = 0f;
                m_FlashlightButton.style.borderLeftWidth = 0f;
            }
        }

        private void SetupTouchLookZone(VisualElement zone)
        {
            zone.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (m_ActiveLookPointerId != -1) return;
                m_ActiveLookPointerId = evt.pointerId;
                m_LastLookPointerPos = evt.position;
                m_AccumulatedLookDelta = Vector2.zero;
                m_HasLookDelta = false;
                IsTouchLookActive = true;
                zone.CapturePointer(evt.pointerId);
            });

            zone.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != m_ActiveLookPointerId) return;

                Vector2 currentPos = evt.position;
                Vector2 delta = currentPos - m_LastLookPointerPos;
                m_LastLookPointerPos = currentPos;

                m_AccumulatedLookDelta += delta;
                m_HasLookDelta = true;
            });

            zone.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId == m_ActiveLookPointerId)
                {
                    zone.ReleasePointer(evt.pointerId);
                    ResetLookTouch();
                }
            });

            zone.RegisterCallback<PointerCancelEvent>(evt =>
            {
                if (evt.pointerId == m_ActiveLookPointerId)
                {
                    zone.ReleasePointer(evt.pointerId);
                    ResetLookTouch();
                }
            });

            zone.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (evt.pointerId == m_ActiveLookPointerId)
                {
                    ResetLookTouch();
                }
            });
        }

        private void ResetLookTouch()
        {
            m_ActiveLookPointerId = -1;
            m_AccumulatedLookDelta = Vector2.zero;
            m_HasLookDelta = false;
            IsTouchLookActive = false;
            LookEvent?.Invoke(Vector2.zero);
        }

        private void Update()
        {
            if (m_ActiveLookPointerId != -1)
            {
                if (m_HasLookDelta)
                {
                    // Direct swipe delta: degrees proportional to pixels dragged
                    float lookX = m_AccumulatedLookDelta.x * (TouchSensitivity * 0.12f);
                    float lookY = -m_AccumulatedLookDelta.y * (TouchSensitivity * 0.12f);

                    if (InvertLookY)
                        lookY = -lookY;

                    LookEvent?.Invoke(new Vector2(lookX, lookY) * LookMagnitudeMultiplier);
                    m_AccumulatedLookDelta = Vector2.zero;
                    m_HasLookDelta = false;
                }
                else
                {
                    LookEvent?.Invoke(Vector2.zero);
                }
            }
        }
    }

    public class VirtualJoystick
    {
        public VisualElement BaseElement;
        public VisualElement Thumbstick;

        public UnityEvent<Vector2> JoystickEvent = new();

        public VirtualJoystick(VisualElement root)
        {
            BaseElement = root;
            Thumbstick = root.Q<VisualElement>("JoystickHandle");

            BaseElement.RegisterCallback<PointerDownEvent>(HandlePress);
            BaseElement.RegisterCallback<PointerMoveEvent>(HandleDrag);
            BaseElement.RegisterCallback<PointerUpEvent>(HandleRelease);
        }

        void HandlePress(PointerDownEvent evt)
        {
            BaseElement.CapturePointer(evt.pointerId);
        }

        void HandleRelease(PointerUpEvent evt)
        {
            BaseElement.ReleasePointer(evt.pointerId);

            Thumbstick.style.left = Length.Percent(50);
            Thumbstick.style.top = Length.Percent(50);

            JoystickEvent.Invoke(Vector2.zero);
        }

        void HandleDrag(PointerMoveEvent evt)
        {
            if (!BaseElement.HasPointerCapture(evt.pointerId)) return;

            var width = BaseElement.contentRect.width;
            var center = new Vector3(width / 2, width / 2);
            var centerToPosition = evt.localPosition - center;

            if (centerToPosition.magnitude > width / 2)
            {
                centerToPosition = centerToPosition.normalized * width / 2;
            }

            var newPos = center + centerToPosition;

            Thumbstick.style.left = newPos.x;
            Thumbstick.style.top = newPos.y;

            centerToPosition /= (width / 2);
            //we invert y as the y of UI goes down, but pushing the joystick up is expected to give a positive y value
            centerToPosition.y *= -1;

            JoystickEvent.Invoke(centerToPosition);
        }
    }
}
