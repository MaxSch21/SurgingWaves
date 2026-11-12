using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace SurgingWaves
{
    /// <summary>Follows four water samples with limited, damped height, pitch and roll.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // After navigation, before player movement and interactions.
    public sealed class BoatWaveMotion : MonoBehaviour
    {
        [SerializeField] private WaterSurface waterSurface;

        [Header("Water Samples")]
        [SerializeField, Tooltip("Centre of the sample rectangle in the boat's local X/Z coordinates.")]
        private Vector2 sampleCenter;
        [SerializeField, Tooltip("Width (X) and length (Z) of the sample rectangle in local units.")]
        private Vector2 sampleSize = new Vector2(4f, 10f);

        [Header("Motion")]
        [SerializeField] private bool heightMotionEnabled = true;
        [SerializeField] private bool tiltMotionEnabled = true;
        [SerializeField, Tooltip("Additional height above the initial waterline, in metres. Initial boat placement defines its immersion.")]
        private float heightOffset;
        [SerializeField, Min(0f), Tooltip("Maximum wave-driven height change from the initial waterline, in metres.")]
        private float maxHeave = 0.5f;
        [SerializeField, Range(0f, 45f)] private float maxPitch = 3f;
        [SerializeField, Range(0f, 45f)] private float maxRoll = 5f;
        [SerializeField, Min(0.01f), Tooltip("Response time in seconds. Larger values make the boat follow waves more slowly.")]
        private float responseTime = 0.6f;

        private Vector3 restPosition;
        private Quaternion restRotation;
        private float initialWaterHeight;
        private bool initialized;

        // Sea steering needs a fixed reference even while the helm moves with the hull.
        public Vector3 NavigationPivot => restPosition;
        public bool HeightMotionEnabled { get => heightMotionEnabled; set => heightMotionEnabled = value; }
        public bool TiltMotionEnabled { get => tiltMotionEnabled; set => tiltMotionEnabled = value; }

        private void Awake()
        {
            restPosition = transform.position;
            restRotation = transform.rotation;
            initialWaterHeight = waterSurface != null ? waterSurface.transform.position.y : restPosition.y;
            initialized = true;
        }

        private void Update()
        {
            if (Time.deltaTime <= 0f) return;

            // Disabled axes return to rest even if water data is temporarily unavailable.
            // Enabled axes hold their previous pose until all four samples are available.
            Vector3 targetPosition = heightMotionEnabled ? transform.position : restPosition;
            Quaternion targetRotation = tiltMotionEnabled ? transform.rotation : restRotation;
            if ((heightMotionEnabled || tiltMotionEnabled) && waterSurface != null && waterSurface.isActiveAndEnabled &&
                TryGetHeight(-1f, 1f, out float frontLeft) && TryGetHeight(1f, 1f, out float frontRight) &&
                TryGetHeight(-1f, -1f, out float backLeft) && TryGetHeight(1f, -1f, out float backRight))
            {
                if (tiltMotionEnabled)
                {
                    Vector3 width = transform.TransformVector(Vector3.right * Mathf.Max(0.1f, sampleSize.x));
                    Vector3 length = transform.TransformVector(Vector3.forward * Mathf.Max(0.1f, sampleSize.y));
                    float horizontalWidth = Mathf.Max(0.01f, new Vector2(width.x, width.z).magnitude);
                    float horizontalLength = Mathf.Max(0.01f, new Vector2(length.x, length.z).magnitude);
                    float pitch = -Mathf.Atan2((frontLeft + frontRight - backLeft - backRight) * 0.5f, horizontalLength) * Mathf.Rad2Deg;
                    float roll = Mathf.Atan2((frontRight + backRight - frontLeft - backLeft) * 0.5f, horizontalWidth) * Mathf.Rad2Deg;
                    pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
                    roll = Mathf.Clamp(roll, -maxRoll, maxRoll);
                    targetRotation = restRotation * Quaternion.Euler(pitch, 0f, roll);
                }

                if (heightMotionEnabled)
                {
                    float averageHeight = (frontLeft + frontRight + backLeft + backRight) * 0.25f;
                    Vector3 centerOffset = Vector3.Scale(new Vector3(sampleCenter.x, 0f, sampleCenter.y), transform.lossyScale);
                    float centerRise = (targetRotation * centerOffset - restRotation * centerOffset).y;
                    float heave = Mathf.Clamp(averageHeight - initialWaterHeight - centerRise, -maxHeave, maxHeave);
                    targetPosition = restPosition + Vector3.up * (heave + heightOffset);
                }
            }
            float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, responseTime));
            ApplyPose(Vector3.Lerp(transform.position, targetPosition, blend),
                Quaternion.Slerp(transform.rotation, targetRotation, blend));
        }

        private Vector3 GetSamplePoint(float side, float end)
        {
            return transform.TransformPoint(new Vector3(
                sampleCenter.x + side * Mathf.Max(0.1f, sampleSize.x) * 0.5f, 0f,
                sampleCenter.y + end * Mathf.Max(0.1f, sampleSize.y) * 0.5f));
        }

        private bool TryGetHeight(float side, float end, out float height)
        {
            Vector3 point = GetSamplePoint(side, end);
            var search = new WaterSearchParameters
            {
                targetPositionWS = point,
                startPositionWS = point,
                error = 0.01f,
                maxIterations = 8,
                includeDeformation = true
            };
            height = 0f;
            if (!waterSurface.ProjectPointOnWaterSurface(search, out WaterSearchResult result)) return false;
            height = result.projectedPositionWS.y;
            return !float.IsNaN(height) && !float.IsInfinity(height);
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            // Update deck colliders before the child player controllers process movement.
            Physics.SyncTransforms();
        }

        private void OnDisable()
        {
            if (initialized) ApplyPose(restPosition, restRotation);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 frontLeft = GetSamplePoint(-1f, 1f);
            Vector3 frontRight = GetSamplePoint(1f, 1f);
            Vector3 backLeft = GetSamplePoint(-1f, -1f);
            Vector3 backRight = GetSamplePoint(1f, -1f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(frontLeft, frontRight);
            Gizmos.DrawLine(frontRight, backRight);
            Gizmos.DrawLine(backRight, backLeft);
            Gizmos.DrawLine(backLeft, frontLeft);
            Gizmos.DrawSphere(frontLeft, 0.06f);
            Gizmos.DrawSphere(frontRight, 0.06f);
            Gizmos.DrawSphere(backLeft, 0.06f);
            Gizmos.DrawSphere(backRight, 0.06f);
        }
    }
}
