using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SurgingWaves.FishCrane
{
    public sealed class FishCraneControl : MonoBehaviour, IXRHoverFilter, IXRSelectFilter
    {
        public enum ControlType { TurnHandle, Crank }

        [SerializeField] private FishCraneController crane;
        [SerializeField] private ControlType controlType;
        [SerializeField, Min(0.1f)] private float grabDistance = 3f;
        [SerializeField] private float turnDegreesPerPixel = 0.3f;
        [SerializeField] private float crankDegreesPerPixel = 2f;

        [Header("VR Interaction")]
        [SerializeField, Min(0f), Tooltip("Maximum hand distance from an interaction collider when starting a direct grab, in metres.")]
        private float vrGrabPadding = 0.1f;
        [SerializeField, Min(0.001f), Tooltip("Ignore hand movement this close to the rotation axis, in metres.")]
        private float minimumVRGrabRadius = 0.025f;

        private Vector3 grabLocalPoint;
        private bool held;
        private XRSimpleInteractable vrInteractable;
        private IXRSelectInteractor vrInteractor;
        private Transform vrHand;
        private Vector3 previousVRDirection;
        private bool hasPreviousVRDirection;

        public bool IsHeld => held;
        public float GrabDistance => grabDistance;
        public Vector3 GrabPoint => transform.TransformPoint(grabLocalPoint);
        public bool CanInteract => isActiveAndEnabled && crane != null
            && (controlType == ControlType.TurnHandle ? crane.CanTurn : crane.CanCrank);
        public string Hint => crane == null ? string.Empty
            : controlType == ControlType.TurnHandle ? crane.TurnHint : crane.CrankHint;

        // Keep rejecting selection when only this control component is disabled.
        public bool canProcess => true;

        private void Awake()
        {
            vrInteractable = GetComponent<XRSimpleInteractable>();
            if (vrInteractable == null) return;
            vrInteractable.hoverFilters.Add(this);
            vrInteractable.selectFilters.Add(this);
        }

        private void OnEnable()
        {
            if (vrInteractable == null) return;
            vrInteractable.selectEntered.AddListener(OnVRGrab);
            vrInteractable.selectExited.AddListener(OnVRRelease);
        }

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable) => CanUseVR(interactor);
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => CanUseVR(interactor);

        private bool CanUseVR(IXRInteractor interactor)
        {
            return isActiveAndEnabled && crane != null && crane.isActiveAndEnabled && (held
                ? interactor == vrInteractor
                : CanInteract && TryGetVRGrabPoint(interactor, out _));
        }

        private bool TryGetVRGrabPoint(IXRInteractor interactor, out Vector3 point)
        {
            point = default;
            if (vrInteractable == null || (!(interactor is XRDirectInteractor) && !(interactor is NearFarInteractor)))
                return false;

            // Near-Far's distant attach anchor is not the tracked hand position.
            Vector3 handPosition = interactor.transform.position;
            foreach (Collider interactionCollider in vrInteractable.colliders)
            {
                if (interactionCollider == null || !interactionCollider.enabled || !interactionCollider.gameObject.activeInHierarchy)
                    continue;
                point = interactionCollider.ClosestPoint(handPosition);
                if ((point - handPosition).sqrMagnitude <= vrGrabPadding * vrGrabPadding) return true;
            }
            return false;
        }

        private void OnVRGrab(SelectEnterEventArgs args)
        {
            if (!TryGetVRGrabPoint(args.interactorObject, out Vector3 point) || !BeginGrab(point))
            {
                vrInteractable.interactionManager.SelectExit(args.interactorObject, vrInteractable);
                return;
            }

            vrInteractor = args.interactorObject;
            vrHand = vrInteractor.transform;
            hasPreviousVRDirection = TryGetVRDirection(out previousVRDirection, out _);
        }

        private void OnVRRelease(SelectExitEventArgs args)
        {
            if (args.interactorObject == vrInteractor) EndGrab();
        }

        private void LateUpdate()
        {
            if (vrInteractor == null) return;
            if (vrHand == null || crane == null || !crane.isActiveAndEnabled || vrInteractable == null ||
                !vrInteractable.isActiveAndEnabled || !vrInteractable.IsSelected(vrInteractor))
            {
                CancelVRGrab();
                return;
            }

            if (!TryGetVRDirection(out Vector3 direction, out Vector3 axis))
            {
                hasPreviousVRDirection = false;
                return;
            }

            if (hasPreviousVRDirection && CanInteract)
            {
                float degrees = Vector3.SignedAngle(previousVRDirection, direction, axis);
                if (controlType == ControlType.TurnHandle) crane.Turn(degrees);
                else crane.RotateCrank(degrees);
            }

            // Discard motion at end stops and during the wait phase.
            previousVRDirection = direction;
            hasPreviousVRDirection = true;
        }

        private bool TryGetVRDirection(out Vector3 direction, out Vector3 axis)
        {
            bool turning = controlType == ControlType.TurnHandle;
            Transform pivot = turning ? crane.transform : crane.Crank;
            direction = axis = Vector3.zero;
            if (pivot == null) return false;

            // Measure in the pivot parent's frame, which does not rotate with the part being operated.
            Transform frame = pivot.parent;
            Vector3 handInFrame = frame != null ? frame.InverseTransformPoint(vrHand.position) : vrHand.position;
            axis = pivot.localRotation * (turning ? Vector3.up : crane.CrankLocalAxis);
            direction = Vector3.ProjectOnPlane(handInFrame - pivot.localPosition, axis);
            Vector3 radialOffset = frame != null ? frame.TransformVector(direction) : direction;
            return radialOffset.sqrMagnitude >= minimumVRGrabRadius * minimumVRGrabRadius;
        }

        private void CancelVRGrab()
        {
            if (vrInteractor != null && vrInteractable != null && vrInteractable.IsSelected(vrInteractor) &&
                vrInteractable.interactionManager != null)
                vrInteractable.interactionManager.SelectExit(vrInteractor, vrInteractable);
            EndGrab();
        }

        public bool BeginGrab(Vector3 worldPoint)
        {
            if (held || !CanInteract) return false;
            bool started = controlType == ControlType.TurnHandle ? crane.BeginTurn() : crane.BeginCrank();
            if (!started) return false;
            grabLocalPoint = transform.InverseTransformPoint(worldPoint);
            held = true;
            return true;
        }

        public void ApplyMouseDelta(Vector2 delta)
        {
            if (!held || !CanInteract) return;
            if (controlType == ControlType.TurnHandle) crane.Turn(delta.x * turnDegreesPerPixel);
            else crane.RotateCrank(delta.y * crankDegreesPerPixel);
        }

        public void EndGrab()
        {
            bool wasHeld = held;
            held = false;
            vrInteractor = null;
            vrHand = null;
            hasPreviousVRDirection = false;
            if (!wasHeld || crane == null) return;
            if (controlType == ControlType.TurnHandle) crane.EndTurn();
            else crane.EndCrank();
        }

        private void OnDisable()
        {
            CancelVRGrab();
            if (vrInteractable == null) return;
            vrInteractable.selectEntered.RemoveListener(OnVRGrab);
            vrInteractable.selectExited.RemoveListener(OnVRRelease);
        }

        private void OnDestroy()
        {
            if (vrInteractable == null) return;
            vrInteractable.hoverFilters.Remove(this);
            vrInteractable.selectFilters.Remove(this);
        }
    }
}
