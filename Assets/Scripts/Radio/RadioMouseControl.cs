using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SurgingWaves.Radio
{
    public sealed class RadioMouseControl : MonoBehaviour, IXRHoverFilter, IXRSelectFilter
    {
        public enum ControlType { PowerKnob, StationButton }

        [SerializeField] private ControlType controlType;
        [SerializeField] private RadioController radio;
        [SerializeField] private Transform visual;
        [SerializeField, Min(0.1f)] private float grabDistance = 2f;

        [Header("Drehknopf")]
        [SerializeField, Min(1f), Tooltip("Horizontale Mauspixel für die vollständige Drehung zwischen Aus und Ein.")]
        private float notchResistance = 60f;
        [SerializeField] private float onAngle = -90f;
        [SerializeField, Min(1f), Tooltip("Rücklauf zur bisherigen Rastposition beim Loslassen, in Grad pro Sekunde.")]
        private float returnSpeed = 360f;

        [Header("Sendertaste")]
        [SerializeField] private Vector3 pressOffset = new Vector3(0f, 0f, 0.008f);

        [Header("VR Interaction")]
        [SerializeField, Min(0f), Tooltip("Maximum hand distance from an interaction collider when starting a direct grab, in metres.")]
        private float vrGrabPadding = 0.05f;

        private Vector3 restPosition;
        private Quaternion restRotation;
        private float knobPosition;
        private bool held;
        private XRSimpleInteractable vrInteractable;
        private IXRSelectInteractor vrInteractor;
        private Transform vrHand;
        private Quaternion previousVRHandRotation;

        public float GrabDistance => grabDistance;
        public bool IsHeld => held;
        public bool IsPowerKnob => controlType == ControlType.PowerKnob;
        public bool CanInteract => isActiveAndEnabled && radio != null && radio.isActiveAndEnabled
            && (IsPowerKnob || radio.IsOn);
        public string Hint => !IsPowerKnob ? "E druecken: Naechster Radiosender"
            : held ? "E halten + Maus links/rechts | Radio: " + (radio.IsOn ? "Ein" : "Aus")
            : "E gedrueckt halten: Radio Ein/Aus";

        // Keep rejecting selection when only this control component is disabled.
        public bool canProcess => true;

        private void Awake()
        {
            knobPosition = radio != null && radio.IsOn ? 1f : 0f;
            if (visual != null)
            {
                restPosition = visual.localPosition;
                restRotation = visual.localRotation;
            }

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
            return CanInteract && (!IsPowerKnob || visual != null) && (held
                ? interactor == vrInteractor
                : TryGetVRGrabPoint(interactor));
        }

        private bool TryGetVRGrabPoint(IXRInteractor interactor)
        {
            if (vrInteractable == null || (!(interactor is XRDirectInteractor) && !(interactor is NearFarInteractor)))
                return false;

            Vector3 handPosition = interactor.transform.position;
            foreach (Collider interactionCollider in vrInteractable.colliders)
            {
                if (interactionCollider == null || !interactionCollider.enabled || !interactionCollider.gameObject.activeInHierarchy)
                    continue;
                Vector3 point = interactionCollider.ClosestPoint(handPosition);
                if ((point - handPosition).sqrMagnitude <= vrGrabPadding * vrGrabPadding) return true;
            }
            return false;
        }

        private void OnVRGrab(SelectEnterEventArgs args)
        {
            if (held || !CanUseVR(args.interactorObject))
            {
                vrInteractable.interactionManager.SelectExit(args.interactorObject, vrInteractable);
                return;
            }

            BeginGrab();
            vrInteractor = args.interactorObject;
            vrHand = vrInteractor.transform;
            if (IsPowerKnob) previousVRHandRotation = GetVRHandRotation();
        }

        private void OnVRRelease(SelectExitEventArgs args)
        {
            if (args.interactorObject == vrInteractor) EndGrab();
        }

        private void UpdateVRInput()
        {
            if (vrInteractor == null) return;
            if (vrHand == null || !CanInteract || (IsPowerKnob && visual == null) || vrInteractable == null ||
                !vrInteractable.isActiveAndEnabled || !vrInteractable.IsSelected(vrInteractor))
            {
                CancelVRGrab();
                return;
            }
            if (!IsPowerKnob) return;

            Quaternion rotation = GetVRHandRotation();
            Quaternion delta = rotation * Quaternion.Inverse(previousVRHandRotation);
            // Extract wrist twist around the knob's resting local Z axis, ignoring tilt.
            float degrees = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(delta.z, delta.w) * Mathf.Rad2Deg);
            previousVRHandRotation = rotation;
            if (Mathf.Abs(onAngle) > 0.001f) ApplyKnobDelta(degrees / onAngle);
        }

        private Quaternion GetVRHandRotation()
        {
            // The knob's visual may be this object itself, so use its parent's resting frame.
            Transform frame = visual.parent;
            Quaternion handRotation = frame != null ? Quaternion.Inverse(frame.rotation) * vrHand.rotation : vrHand.rotation;
            return Quaternion.Inverse(restRotation) * handRotation;
        }

        private void CancelVRGrab()
        {
            if (vrInteractor != null && vrInteractable != null && vrInteractable.IsSelected(vrInteractor) &&
                vrInteractable.interactionManager != null)
                vrInteractable.interactionManager.SelectExit(vrInteractor, vrInteractable);
            EndGrab();
        }

        private void LateUpdate()
        {
            UpdateVRInput();
            if (IsPowerKnob && radio != null && (!held || !CanInteract))
                knobPosition = Mathf.MoveTowards(knobPosition, radio.IsOn ? 1f : 0f,
                    returnSpeed / Mathf.Max(1f, Mathf.Abs(onAngle)) * Time.deltaTime);
            RefreshVisual();
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

        public void BeginGrab()
        {
            if (held || !CanInteract) return;
            held = true;
            if (!IsPowerKnob) radio.NextStation();
            RefreshVisual();
        }

        public void EndGrab()
        {
            held = false;
            vrInteractor = null;
            vrHand = null;
            RefreshVisual();
        }

        public void ApplyMouseDelta(Vector2 delta)
        {
            if (!held || !CanInteract || !IsPowerKnob) return;
            ApplyKnobDelta(delta.x / Mathf.Max(1f, notchResistance));
            RefreshVisual();
        }

        private void ApplyKnobDelta(float delta)
        {
            knobPosition = Mathf.Clamp01(knobPosition + delta);
            if (knobPosition >= 1f) radio.SetPower(true);
            else if (knobPosition <= 0f) radio.SetPower(false);
        }

        private void RefreshVisual()
        {
            if (visual == null || radio == null) return;
            if (IsPowerKnob)
                visual.localRotation = restRotation * Quaternion.AngleAxis(knobPosition * onAngle, Vector3.forward);
            else
                visual.localPosition = restPosition + (held && CanInteract ? pressOffset : Vector3.zero);
        }
    }
}
