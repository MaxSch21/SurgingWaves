using UnityEngine;

namespace SurgingWaves.Interaction
{
    public sealed class HingedDoor : MonoBehaviour
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

        public float GrabDistance => grabDistance;
        public float CurrentAngle => currentAngle;
        public bool IsHeld => held;
        public Vector3 GrabPoint => transform.TransformPoint(grabLocalPoint);
        public string Label => motionType == MotionType.FloorHatch ? "Hatch" : "Door";

        private float LowerLimit => Mathf.Min(minAngle, maxAngle);
        private float UpperLimit => Mathf.Max(minAngle, maxAngle);

        private void Awake()
        {
            startRotation = transform.localRotation;
            scaledHinge = Vector3.Scale(hingeLocalPosition, transform.localScale);
            hingeInParent = transform.localPosition + startRotation * scaledHinge;
            axis = hingeLocalAxis.sqrMagnitude > 0.0001f ? hingeLocalAxis.normalized : Vector3.up;
            SetAngle(0f);
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
            grabLocalPoint = transform.InverseTransformPoint(worldPoint);
            angularVelocity = 0f;
            held = true;
            return true;
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
        }

        private void OnDisable() => EndGrab();

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
