using StarterAssets;
using UnityEngine;

namespace StarterAssets
{
    public class VirtualInput : MonoBehaviour
    {
        [Header("Output")]
        public StarterAssetsInputs StarterAssetsInputs;

        private void Awake()
        {
            FindInputsIfNull();
        }

        private void OnEnable()
        {
            FindInputsIfNull();
        }

        private void FindInputsIfNull()
        {
            if (StarterAssetsInputs == null)
            {
                StarterAssetsInputs = FindAnyObjectByType<StarterAssetsInputs>();
            }
        }

        public void VirtualMoveInput(Vector2 virtualMoveDirection)
        {
            if (StarterAssetsInputs != null)
                StarterAssetsInputs.MoveInput(virtualMoveDirection);
        }

        public void VirtualLookInput(Vector2 virtualLookDirection)
        {
            if (StarterAssetsInputs != null)
                StarterAssetsInputs.LookInput(virtualLookDirection);
        }

        public void VirtualJumpInput(bool virtualJumpState)
        {
            if (StarterAssetsInputs != null)
                StarterAssetsInputs.JumpInput(virtualJumpState);
        }

        public void VirtualSprintInput(bool virtualSprintState)
        {
            if (StarterAssetsInputs != null)
                StarterAssetsInputs.SprintInput(virtualSprintState);
        }
    }
}
