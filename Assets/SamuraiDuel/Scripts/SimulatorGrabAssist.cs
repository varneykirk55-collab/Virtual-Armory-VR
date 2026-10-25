#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace SamuraiDuel
{
    /// <summary>
    /// Editor-only testing aid for the XR Device Simulator. Press F to put the nearest free
    /// katana/sword into the right hand (and F again to drop it), so you can test swings without
    /// steering a simulated hand onto the sword and holding G at the same time.
    ///
    /// Uses XRI's manual-interaction API, so the grab goes through the normal XRGrabInteractable
    /// path (velocity tracking, SwordHitDetector, haptics). Created automatically only when an
    /// XR Device Simulator is in the scene, and compiled out of player builds.
    /// </summary>
    public class SimulatorGrabAssist : MonoBehaviour
    {
        private InputAction toggle;
        private XRDirectInteractor hand;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (XRDeviceSimulator.instance == null || FindObjectOfType<SimulatorGrabAssist>() != null)
                return;
            new GameObject("Simulator Grab Assist").AddComponent<SimulatorGrabAssist>();
            Debug.Log("XR Device Simulator detected: press F to grab/drop the nearest sword with the right hand.");
        }

        private void Awake()
        {
            toggle = new InputAction("Simulator Grab Toggle", InputActionType.Button, "<Keyboard>/f");
        }

        private void OnEnable() => toggle.Enable();
        private void OnDisable() => toggle.Disable();
        private void OnDestroy() => toggle.Dispose();

        private void Update()
        {
            if (!toggle.WasPressedThisFrame())
                return;

            if (hand == null)
                hand = FindRightHand();
            if (hand == null)
            {
                Debug.LogWarning("SimulatorGrabAssist: no XRDirectInteractor found for the right hand.");
                return;
            }

            if (hand.isPerformingManualInteraction)
            {
                hand.EndManualInteraction();
                return;
            }
            if (hand.hasSelection)
                return; // already holding something via the simulator's grip (G)

            XRGrabInteractable target = NearestFreeBlade(hand.transform.position);
            if (target == null)
                return;

            SnapToHand(target);
            hand.StartManualInteraction((IXRSelectInteractable)target);
        }

        private static XRDirectInteractor FindRightHand()
        {
            XRDirectInteractor fallback = null;
            foreach (var interactor in FindObjectsOfType<XRDirectInteractor>())
            {
                if (interactor.CompareTag("rightHand") || interactor.name.ToLowerInvariant().Contains("right"))
                    return interactor;
                fallback = interactor;
            }
            return fallback;
        }

        private static XRGrabInteractable NearestFreeBlade(Vector3 from)
        {
            XRGrabInteractable best = null;
            float bestDistance = float.MaxValue;
            var blades = SwordHitDetector.All;
            for (int i = 0; i < blades.Count; i++)
            {
                if (blades[i].Team != CombatTeam.Player || blades[i].IsHeld)
                    continue;
                if (!(blades[i].Interactable is XRGrabInteractable grab))
                    continue;
                float distance = (grab.transform.position - from).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = grab;
                }
            }
            return best;
        }

        /// <summary>
        /// Teleports the weapon so its attach point already matches the hand. Otherwise velocity
        /// tracking would drag it across the room and through the furniture.
        /// </summary>
        private void SnapToHand(XRGrabInteractable target)
        {
            Transform handAttach = hand.attachTransform != null ? hand.attachTransform : hand.transform;
            Transform root = target.transform;
            Transform grip = target.attachTransform != null ? target.attachTransform : root;

            Quaternion gripLocalRotation = Quaternion.Inverse(root.rotation) * grip.rotation;
            Vector3 gripLocalPosition = root.InverseTransformPoint(grip.position);
            Quaternion rotation = handAttach.rotation * Quaternion.Inverse(gripLocalRotation);
            Vector3 position = handAttach.position - rotation * Vector3.Scale(gripLocalPosition, root.lossyScale);

            root.SetPositionAndRotation(position, rotation);
            var body = target.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }
}
#endif
