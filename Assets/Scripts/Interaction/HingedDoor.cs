using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SurgingWaves.Interaction
{
    public sealed class HingedDoor : MonoBehaviour, IXRHoverFilter, IXRSelectFilter
    {
        public enum MotionType { Door, FloorHatch }

        [Header("Bewegung")]
        [SerializeField] private MotionType motionType;
        [SerializeField, Tooltip("Scharnierpunkt in lokalen Koordinaten des bewegten Objekts.")]
        private Vector3 hingeLocalPosition;
        [SerializeField, Tooltip("Drehachse in lokalen Koordinaten des bewegten Objekts.")]
        private Vector3 hingeLocalAxis = Vector3.up;
        [SerializeField, Tooltip("Winkel relativ zur Startposition der Szene.")]
        private float minAngle;
        [SerializeField] private float maxAngle = 135f;

        [Header("Bedienung")]
        [SerializeField, Min(0.1f)] private float grabDistance = 2f;
        [SerializeField, Tooltip("Grad pro horizontalem Mauspixel. Negative Werte kehren die Richtung um.")]
        private float mouseDegreesPerPixel = 0.25f;
        [SerializeField] private Vector3 interactionCenter = new Vector3(0.75f, 1f, 0f);
        [SerializeField] private Vector3 interactionSize = new Vector3(0.5f, 2f, 0.2f);

        [Header("VR Interaction")]
        [SerializeField, Min(0f), Tooltip("Maximum hand distance from the interaction zone when starting a direct grab, in metres.")]
        private float vrGrabPadding = 0.1f;
        [SerializeField, Min(0.001f), Tooltip("Ignore hand movement this close to the hinge axis, in metres, to avoid unstable angles.")]
        private float minimumVRGrabRadius = 0.025f;

        [Header("Leichte Endrastung")]
        [SerializeField, Min(0f), Tooltip("Fangbereich beim Loslassen, in Grad.")]
        private float snapAngle = 3f;
        [SerializeField, Min(0f), Tooltip("Geschwindigkeit der Endrastung in Grad pro Sekunde.")]
        private float snapSpeed = 30f;

        [Header("Bodenklappe")]
        [SerializeField, Tooltip("Kipppunkt relativ zur Startposition. Bei negativem Öffnungswinkel ebenfalls negativ setzen.")]
        private float tippingAngle = 90f;
        [SerializeField, Min(0f), Tooltip("Maximale Fallbeschleunigung in Grad pro Sekunde zum Quadrat.")]
        private float fallStrength = 180f;
        [SerializeField, Min(0f)] private float fallDamping = 1.5f;

        private Vector3 hingeInParent;
        private Vector3 scaledHinge;
        private Vector3 axis;
        private Quaternion startRotation;
        private Vector3 grabLocalPoint;
        private float currentAngle;
        private float angularVelocity;
        private bool held;
        private XRSimpleInteractable vrInteractable;
        private IXRSelectInteractor vrInteractor;
        private Transform vrHand;
        private Vector3 previousVRDirection;
        private bool hasPreviousVRDirection;

        public float GrabDistance => grabDistance;
        public float CurrentAngle => currentAngle;
        public bool IsHeld => held;
        public Vector3 GrabPoint => transform.TransformPoint(grabLocalPoint);
        public string Label => motionType == MotionType.FloorHatch ? "Hatch" : "Door";

        private float LowerLimit => Mathf.Min(minAngle, maxAngle);
        private float UpperLimit => Mathf.Max(minAngle, maxAngle);

        // Keep filtering disabled doors too, while their XRI component is still enabled.
        public bool canProcess => true;

        private void Awake()
        {
            startRotation = transform.localRotation;
            scaledHinge = Vector3.Scale(hingeLocalPosition, transform.localScale);
            hingeInParent = transform.localPosition + startRotation * scaledHinge;
            axis = hingeLocalAxis.sqrMagnitude > 0.0001f ? hingeLocalAxis.normalized : Vector3.up;
            SetAngle(0f);

            // VR is optional; existing FPS-only instances need no additional components.
            vrInteractable = GetComponent<XRSimpleInteractable>();
            if (vrInteractable != null)
            {
                vrInteractable.hoverFilters.Add(this);
                vrInteractable.selectFilters.Add(this);
            }
        }

        private void OnEnable()
        {
            if (vrInteractable == null) return;
            vrInteractable.selectEntered.AddListener(OnVRGrab);
            vrInteractable.selectExited.AddListener(OnVRRelease);
        }

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable)
        {
            return isActiveAndEnabled && (held
                ? interactor == vrInteractor
                : TryGetVRGrabPoint(interactor, out _));
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            return isActiveAndEnabled && (held
                ? interactor == vrInteractor
                : TryGetVRGrabPoint(interactor, out _));
        }

        private bool TryGetVRGrabPoint(IXRInteractor interactor, out Vector3 point)
        {
            point = default;
            if (!(interactor is XRDirectInteractor) && !(interactor is NearFarInteractor)) return false;

            // Use the tracked hand, not Near-Far's movable distant attach anchor.
            Vector3 handPosition = interactor.transform.position;
            Vector3 localPoint = new Bounds(interactionCenter, interactionSize)
                .ClosestPoint(transform.InverseTransformPoint(handPosition));
            point = transform.TransformPoint(localPoint);
            return (point - handPosition).sqrMagnitude <= vrGrabPadding * vrGrabPadding;
        }

        private void OnVRGrab(SelectEnterEventArgs args)
        {
            if (!isActiveAndEnabled || held || !TryGetVRGrabPoint(args.interactorObject, out Vector3 point))
            {
                vrInteractable.interactionManager.SelectExit(args.interactorObject, vrInteractable);
                return;
            }

            // The point was clamped to the zone already; avoid a second bounds check
            // after a world/local round trip, which can round outside its surface.
            StartGrab(point);
            vrInteractor = args.interactorObject;
            vrHand = vrInteractor.transform;
            hasPreviousVRDirection = TryGetVRDirection(out previousVRDirection);
        }

        private void OnVRRelease(SelectExitEventArgs args)
        {
            if (args.interactorObject == vrInteractor) EndGrab();
        }

        private void LateUpdate()
        {
            if (vrInteractor == null) return;
            if (vrHand == null || vrInteractable == null || !vrInteractable.isActiveAndEnabled ||
                !vrInteractable.IsSelected(vrInteractor))
            {
                EndGrab();
                return;
            }

            if (!TryGetVRDirection(out Vector3 direction))
            {
                hasPreviousVRDirection = false;
                return;
            }

            if (hasPreviousVRDirection)
                SetAngle(currentAngle + Vector3.SignedAngle(previousVRDirection, direction, axis));

            // Discard motion past the stops so reversing responds immediately.
            previousVRDirection = direction;
            hasPreviousVRDirection = true;
        }

        private bool TryGetVRDirection(out Vector3 direction)
        {
            Transform parent = transform.parent;
            Vector3 handInParent = parent != null ? parent.InverseTransformPoint(vrHand.position) : vrHand.position;
            direction = Vector3.ProjectOnPlane(Quaternion.Inverse(startRotation) * (handInParent - hingeInParent), axis);
            Vector3 radialOffset = startRotation * direction;
            if (parent != null) radialOffset = parent.TransformVector(radialOffset);
            return radialOffset.sqrMagnitude >= minimumVRGrabRadius * minimumVRGrabRadius;
        }

        private void FixedUpdate()
        {
            if (held) return;

            if (motionType == MotionType.Door)
            {
                SnapToEnd(0f);
                return;
            }

            // For a horizontal hatch: closing torque below 90 degrees,
            // opening torque above 90 degrees. A negative tipping angle
            // supports hatches opening towards negative angles as well.
            float acceleration = fallStrength * Mathf.Sin((currentAngle - tippingAngle) * Mathf.Deg2Rad);
            if (SnapToEnd(acceleration)) return;

            angularVelocity += acceleration * Time.fixedDeltaTime;
            angularVelocity *= Mathf.Exp(-fallDamping * Time.fixedDeltaTime);
            SetAngle(currentAngle + angularVelocity * Time.fixedDeltaTime);
            if (currentAngle <= LowerLimit || currentAngle >= UpperLimit) angularVelocity = 0f;
        }

        public bool CanGrabAt(Vector3 worldPoint)
        {
            return isActiveAndEnabled && !held
                && new Bounds(interactionCenter, interactionSize).Contains(transform.InverseTransformPoint(worldPoint));
        }

        public bool BeginGrab(Vector3 worldPoint)
        {
            if (!CanGrabAt(worldPoint)) return false;
            StartGrab(worldPoint);
            return true;
        }

        private void StartGrab(Vector3 worldPoint)
        {
            grabLocalPoint = transform.InverseTransformPoint(worldPoint);
            angularVelocity = 0f;
            held = true;
        }

        public void ApplyMouseDelta(Vector2 delta)
        {
            if (held && isActiveAndEnabled)
                SetAngle(currentAngle + delta.x * mouseDegreesPerPixel);
        }

        public void EndGrab()
        {
            held = false;
            angularVelocity = 0f;
            vrInteractor = null;
            vrHand = null;
            hasPreviousVRDirection = false;
        }

        private void OnDisable()
        {
            if (vrInteractable != null)
            {
                if (vrInteractor != null && vrInteractable.IsSelected(vrInteractor) && vrInteractable.interactionManager != null)
                    vrInteractable.interactionManager.SelectExit(vrInteractor, vrInteractable);

                vrInteractable.selectEntered.RemoveListener(OnVRGrab);
                vrInteractable.selectExited.RemoveListener(OnVRRelease);
            }
            EndGrab();
        }

        private void OnDestroy()
        {
            if (vrInteractable == null) return;
            vrInteractable.hoverFilters.Remove(this);
            vrInteractable.selectFilters.Remove(this);
        }

        public void SetAngle(float angle)
        {
            currentAngle = Mathf.Clamp(angle, LowerLimit, UpperLimit);
            Quaternion rotation = startRotation * Quaternion.AngleAxis(currentAngle, axis);
            transform.localRotation = rotation;
            transform.localPosition = hingeInParent - rotation * scaledHinge;
        }

        private bool SnapToEnd(float acceleration)
        {
            if (snapAngle <= 0f || snapSpeed <= 0f) return false;
            float toLower = currentAngle - LowerLimit;
            float toUpper = UpperLimit - currentAngle;
            bool snapLower = toLower <= snapAngle && acceleration <= 0f;
            bool snapUpper = toUpper <= snapAngle && acceleration >= 0f;
            if (!snapLower && !snapUpper) return false;

            float target = snapLower && (!snapUpper || toLower <= toUpper) ? LowerLimit : UpperLimit;
            angularVelocity = 0f;
            SetAngle(Mathf.MoveTowards(currentAngle, target, snapSpeed * Time.fixedDeltaTime));
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.2f);
            Gizmos.DrawCube(interactionCenter, interactionSize);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(interactionCenter, interactionSize);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.yellow;
            Vector3 pivot = transform.TransformPoint(hingeLocalPosition);
            Vector3 direction = transform.TransformDirection(hingeLocalAxis.normalized);
            Gizmos.DrawSphere(pivot, 0.015f);
            Gizmos.DrawLine(pivot - direction * 0.25f, pivot + direction * 0.25f);
            Gizmos.matrix = previousMatrix;
        }
    }
}
