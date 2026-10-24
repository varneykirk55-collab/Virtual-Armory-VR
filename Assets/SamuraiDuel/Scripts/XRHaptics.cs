using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace SamuraiDuel
{
    public static class XRHaptics
    {
        /// <summary>Buzz every controller currently holding this interactable.</summary>
        public static void PulseHolders(XRBaseInteractable interactable, float amplitude, float duration)
        {
            if (interactable == null || !interactable.isSelected)
                return;

            var holders = interactable.interactorsSelecting;
            for (int i = 0; i < holders.Count; i++)
            {
                if (holders[i] is XRBaseControllerInteractor controllerInteractor)
                    controllerInteractor.SendHapticImpulse(Mathf.Clamp01(amplitude), duration);
            }
        }

        private static XRBaseController[] cachedControllers;

        /// <summary>Buzz both hands (used when the player takes damage).</summary>
        public static void PulseAll(float amplitude, float duration)
        {
            if (cachedControllers == null || cachedControllers.Length == 0 || cachedControllers[0] == null)
                cachedControllers = Object.FindObjectsOfType<XRBaseController>();

            foreach (var controller in cachedControllers)
            {
                if (controller != null)
                    controller.SendHapticImpulse(Mathf.Clamp01(amplitude), duration);
            }
        }
    }
}
