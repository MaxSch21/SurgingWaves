using System.Collections;
using UnityEngine;

namespace SurgingWaves.Fish
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    [DefaultExecutionOrder(-50)]
    public sealed class FishBodySimulation : MonoBehaviour
    {
        private const int SolverSubsteps = 4;
        private const float TwitchDuration = 0.22f;

        public enum LifeState
        {
            Dead,
            Alive
        }

        [Header("Body")]
        [SerializeField, Range(3, 16)] private int pointCount = 7;
        [SerializeField, Min(0.05f)] private float bodyLength = 0.45f;
        [SerializeField, Min(0.001f), Tooltip("Minimum collision radius; the visual binding can enlarge the collision envelope.")]
        private float bodyRadius = 0.035f;

        [Header("Passive motion")]
        [SerializeField, Tooltip("Applied once to the Rigidbody and once to each free Verlet point. Built-in Rigidbody gravity is disabled while this component is active.")]
        private Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        [SerializeField, Min(0f), Tooltip("Velocity damping per second.")]
        private float damping = 2f;
        [SerializeField, Range(0f, 1f)] private float bodyStiffness = 0.7f;
        [SerializeField, Range(0f, 1f)] private float tailStiffness = 0.08f;
        [SerializeField, Range(1, 24)] private int constraintIterations = 10;

        [Header("Alive motion")]
        [SerializeField] private LifeState state = LifeState.Dead;
        [SerializeField, Min(0f), Tooltip("Body wave cycles per second.")]
        private float wiggleFrequency = 2f;
        [SerializeField, Range(0f, 45f), Tooltip("Body wave bend strength in degrees, increasing toward the tail.")]
        private float wiggleStrength = 14f;
        [SerializeField, Min(0f), Tooltip("Average irregular twitches per second while alive. Set to zero to disable.")]
        private float twitchFrequency = 0.3f;

        [Header("Simple floor")]
        [SerializeField, Tooltip("Only this flat box's top face participates in flexible-body collision. Other collisions belong to the Rigidbody.")]
        private BoxCollider floorCollider;
        [SerializeField, Min(0f), Tooltip("Resistance to rolling while supported on the floor and lying on either side.")]
        private float restingRollDamping = 12f;

        private Rigidbody rootBody;
        private CapsuleCollider headCollider;
        private Vector3[] positions;
        private Vector3[] previousPositions;
        private Vector3[] renderPrevious;
        private float[] radii;
        private float length;
        private float segmentLength;
        private Vector3 lastRootPosition;
        private Quaternion lastRootRotation;
        private bool originalUseGravity;
        private Coroutine simulationLoop;
        private readonly System.Random twitchRandom = new System.Random();
        private float wigglePhase;
        private float twitchTime;
        private float twitchDirection;
        private Vector3 driveAxis;
        private float driveTwitch;

        public int PointCount => positions == null ? 0 : positions.Length;
        public float BodyLength => length;
#if UNITY_EDITOR
        internal int PreviewPointCount => Mathf.Clamp(pointCount, 3, 16);
        internal float PreviewBodyLength => Mathf.Max(0.05f, bodyLength);
#endif
        public LifeState State
        {
            get => state;
            set => state = value;
        }

        private void Awake()
        {
            rootBody = GetComponent<Rigidbody>();
            headCollider = GetComponent<CapsuleCollider>();
            originalUseGravity = rootBody.useGravity;
            length = Mathf.Max(0.05f, bodyLength);
            int count = Mathf.Clamp(pointCount, 3, 16);
            segmentLength = length / (count - 1);
            positions = new Vector3[count];
            previousPositions = new Vector3[count];
            renderPrevious = new Vector3[count];
            radii = new float[count];
            lastRootPosition = rootBody.position;
            lastRootRotation = rootBody.rotation;

            for (int i = 0; i < count; i++)
            {
                Vector3 point = PhysicsAnchor(i);
                positions[i] = renderPrevious[i] = point;
                previousPositions[i] = point - rootBody.GetPointVelocity(point) * (Time.fixedDeltaTime / SolverSubsteps);
            }
            SetVisualRadii(null);
        }

        private void OnEnable()
        {
            rootBody.useGravity = false;
            simulationLoop = StartCoroutine(SimulateAfterPhysics());
        }

        private void OnDisable()
        {
            if (simulationLoop != null) StopCoroutine(simulationLoop);
            simulationLoop = null;
            if (rootBody != null) rootBody.useGravity = originalUseGravity;
        }

        private void FixedUpdate()
        {
            // Unity integrates the core. The assigned floor also protects manually moved head anchors.
            if (!rootBody.isKinematic && !rootBody.IsSleeping())
                rootBody.AddForce(gravity, ForceMode.Acceleration);
        }

        private IEnumerator SimulateAfterPhysics()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                Simulate(Time.fixedDeltaTime);
            }
        }

        private Vector3 PhysicsAnchor(int index)
        {
            return rootBody.position + rootBody.rotation * (Vector3.back * (index * segmentLength));
        }

        private void PinHead(Vector3 rootPosition, Quaternion rootRotation)
        {
            for (int i = 0; i < 2; i++)
                positions[i] = previousPositions[i] = rootPosition + rootRotation * (Vector3.back * (i * segmentLength));
        }

        private static float InverseMass(int index) => index < 2 ? 0f : 1f;

        private void Simulate(float dt)
        {
            if (dt <= 0f) return;
            Vector3 rootPosition = rootBody.position;
            Quaternion rootRotation = rootBody.rotation;
            bool hasFloor = TryGetFloor(out Vector3 floorPoint, out Vector3 floorNormal);
            if (hasFloor) ProtectHeadFromFloor(ref rootPosition, rootRotation, floorPoint, floorNormal);
            // Large discontinuities are teleports, not movement to integrate twice.
            if (Vector3.Distance(rootPosition, lastRootPosition) > Mathf.Max(1f, length * 3f)
                || Quaternion.Angle(rootRotation, lastRootRotation) > 120f)
            {
                Quaternion change = rootRotation * Quaternion.Inverse(lastRootRotation);
                for (int i = 0; i < PointCount; i++)
                {
                    positions[i] = rootPosition + change * (positions[i] - lastRootPosition);
                    previousPositions[i] = rootPosition + change * (previousPositions[i] - lastRootPosition);
                    renderPrevious[i] = rootPosition + change * (renderPrevious[i] - lastRootPosition);
                }
                lastRootPosition = rootPosition;
                lastRootRotation = rootRotation;
            }
            System.Array.Copy(positions, renderPrevious, PointCount);

            float step = dt / SolverSubsteps;
            float drag = Mathf.Exp(-Mathf.Max(0f, damping) * step);
            int iterations = Mathf.Clamp(constraintIterations, 1, 24);
            for (int substep = 0; substep < SolverSubsteps; substep++)
            {
                float fraction = (substep + 1f) / SolverSubsteps;
                Vector3 anchorPosition = Vector3.Lerp(lastRootPosition, rootPosition, fraction);
                Quaternion anchorRotation = Quaternion.Slerp(lastRootRotation, rootRotation, fraction);
                UpdateAliveMotion(step, anchorRotation);
                for (int i = 2; i < PointCount; i++)
                {
                    Vector3 current = positions[i];
                    positions[i] += (current - previousPositions[i]) * drag + gravity * (step * step);
                    previousPositions[i] = current;
                }

                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    PinHead(anchorPosition, anchorRotation);
                    for (int i = 0; i < PointCount - 2; i++) SolveBend(i, iterations);
                    for (int i = 0; i < PointCount - 1; i++) SolveDistance(i, i + 1);
                    if (hasFloor)
                        for (int i = 2; i < PointCount; i++) ProjectFloor(i, floorPoint, floorNormal);
                }
                if (hasFloor)
                    ResolveFloorVelocity(floorPoint, floorNormal, step);
            }
            PinHead(rootPosition, rootRotation);
            lastRootPosition = rootPosition;
            lastRootRotation = rootRotation;
            if (hasFloor) DampenRestingRoll(floorPoint, floorNormal, dt);
        }

        private void ProtectHeadFromFloor(ref Vector3 rootPosition, Quaternion rootRotation, Vector3 floorPoint, Vector3 normal)
        {
            float penetration = 0f;
            for (int i = 0; i < 2; i++)
            {
                Vector3 point = rootPosition + rootRotation * (Vector3.back * (i * segmentLength));
                if (OverFloor(point)) penetration = Mathf.Max(penetration, radii[i] - Vector3.Dot(point - floorPoint, normal));
            }
            if (penetration <= 0f) return;
            // Transform gizmos and kinematic movement can bypass Unity's collision response.
            rootPosition += normal * penetration;
            rootBody.position = rootPosition;
            if (!rootBody.isKinematic)
            {
                float inwardSpeed = Mathf.Min(0f, Vector3.Dot(rootBody.linearVelocity, normal));
                rootBody.linearVelocity -= normal * inwardSpeed;
            }
        }

        private void ResolveFloorVelocity(Vector3 floorPoint, Vector3 normal, float step)
        {
            float friction = Mathf.Exp(-8f * step);
            for (int i = 2; i < PointCount; i++)
            {
                if (!OverFloor(positions[i]) || Vector3.Dot(positions[i] - floorPoint, normal) > radii[i] + 0.001f)
                    continue;
                Vector3 displacement = positions[i] - previousPositions[i];
                float outwardMotion = Mathf.Max(0f, Vector3.Dot(displacement, normal));
                Vector3 retainedMotion = Vector3.ProjectOnPlane(displacement, normal) * friction + normal * outwardMotion;
                previousPositions[i] = positions[i] - retainedMotion;
            }
        }

        private void DampenRestingRoll(Vector3 floorPoint, Vector3 normal, float dt)
        {
            if (rootBody.isKinematic || restingRollDamping <= 0f || !OverFloor(positions[0])) return;
            if (Vector3.Dot(positions[0] - floorPoint, normal) > headCollider.radius + 0.01f) return;
            if (Mathf.Abs(Vector3.Dot(rootBody.rotation * Vector3.up, normal)) > 0.4f) return;
            Vector3 axis = rootBody.rotation * Vector3.forward;
            float roll = Vector3.Dot(rootBody.angularVelocity, axis);
            if (Mathf.Abs(roll) < 0.0001f) return;
            float retainedRoll = roll * Mathf.Exp(-restingRollDamping * dt);
            if (Mathf.Abs(retainedRoll) < 0.03f) retainedRoll = 0f;
            rootBody.angularVelocity += axis * (retainedRoll - roll);
        }

        private void SolveDistance(int a, int b)
        {
            float wa = InverseMass(a), wb = InverseMass(b);
            if (wa + wb == 0f) return;
            Vector3 delta = positions[b] - positions[a];
            float distance = delta.magnitude;
            if (distance < 0.000001f) return;
            Vector3 correction = delta * ((distance - segmentLength) / (distance * (wa + wb)));
            positions[a] += correction * wa;
            positions[b] -= correction * wb;
        }

        private void SolveBend(int a, int iterations)
        {
            int b = a + 1, c = a + 2;
            float wa = InverseMass(a), wb = InverseMass(b), wc = InverseMass(c);
            float weight = wa + 4f * wb + wc;
            float stiffness = Mathf.Lerp(bodyStiffness, tailStiffness, b / (float)(PointCount - 1));
            float perIteration = 1f - Mathf.Pow(1f - Mathf.Clamp01(stiffness), 1f / (iterations * SolverSubsteps));
            Vector3 targetCurvature = Vector3.zero;
            if (state == LifeState.Alive)
            {
                float bodyFraction = b / (float)(PointCount - 1);
                float wave = Mathf.Sin(wigglePhase - bodyFraction * Mathf.PI * 2f);
                float bend = (wave + driveTwitch) * Mathf.Clamp(wiggleStrength, 0f, 45f) * Mathf.Deg2Rad * bodyFraction;
                targetCurvature = driveAxis * (2f * segmentLength * Mathf.Sin(bend * 0.5f));
            }
            // Alive movement changes the soft rest curvature within the same passive solver.
            Vector3 correction = (positions[a] - 2f * positions[b] + positions[c] - targetCurvature) * (perIteration / weight);
            positions[a] -= correction * wa;
            positions[b] += correction * (2f * wb);
            positions[c] -= correction * wc;
        }

        private void UpdateAliveMotion(float step, Quaternion anchorRotation)
        {
            driveAxis = anchorRotation * Vector3.right;
            driveTwitch = 0f;
            if (state != LifeState.Alive)
            {
                twitchTime = 0f;
                return;
            }
            wigglePhase = Mathf.Repeat(wigglePhase + Mathf.Max(0f, wiggleFrequency) * Mathf.PI * 2f * step, Mathf.PI * 2f);
            float probability = 1f - Mathf.Exp(-Mathf.Max(0f, twitchFrequency) * step);
            if (twitchTime <= 0f && twitchRandom.NextDouble() < probability)
            {
                twitchTime = TwitchDuration;
                twitchDirection = twitchRandom.Next(2) == 0 ? -1f : 1f;
            }
            if (twitchTime <= 0f) return;
            float progress = 1f - twitchTime / TwitchDuration;
            driveTwitch = twitchDirection * 1.5f * Mathf.Sin(progress * Mathf.PI);
            twitchTime = Mathf.Max(0f, twitchTime - step);
        }

        private bool TryGetFloor(out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.up;
            if (floorCollider == null || !floorCollider.enabled || !floorCollider.gameObject.activeInHierarchy) return false;
            Transform floor = floorCollider.transform;
            point = floor.TransformPoint(floorCollider.center + Vector3.up * (floorCollider.size.y * 0.5f));
            normal = floor.up;
            return true;
        }

        private bool OverFloor(Vector3 point)
        {
            Vector3 local = floorCollider.transform.InverseTransformPoint(point) - floorCollider.center;
            Vector3 half = floorCollider.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
        }

        private void ProjectFloor(int index, Vector3 point, Vector3 normal)
        {
            if (!OverFloor(positions[index])) return;
            float penetration = radii[index] - Vector3.Dot(positions[index] - point, normal);
            if (penetration > 0f) positions[index] += normal * penetration;
        }

        public Vector3 GetRenderPoint(int index)
        {
            if (index < 2) return transform.TransformPoint(Vector3.back * (index * segmentLength));
            float alpha = rootBody.interpolation == RigidbodyInterpolation.None ? 1f
                : Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            return Vector3.Lerp(renderPrevious[index], positions[index], alpha);
        }

        public void SetVisualRadii(float[] visualRadii)
        {
            for (int i = 0; i < PointCount; i++)
            {
                float minimum = bodyRadius * Mathf.Lerp(1f, 0.25f, i / (float)(PointCount - 1));
                radii[i] = visualRadii != null && i < visualRadii.Length ? Mathf.Max(minimum, visualRadii[i]) : minimum;
            }
            headCollider.direction = 2;
            headCollider.center = Vector3.back * (segmentLength * 0.5f);
            headCollider.radius = Mathf.Max(radii[0], radii[1]);
            headCollider.height = segmentLength + headCollider.radius * 2f;
        }

        private void OnDrawGizmosSelected()
        {
            if (positions == null) return;
            for (int i = 0; i < PointCount; i++)
            {
                Gizmos.color = i < 2 ? Color.yellow : Color.cyan;
                Gizmos.DrawWireSphere(positions[i], radii[i]);
                if (i > 0) Gizmos.DrawLine(positions[i - 1], positions[i]);
            }
        }
    }
}
