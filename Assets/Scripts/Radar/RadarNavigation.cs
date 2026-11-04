using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace SurgingWaves.Radar
{
    [DefaultExecutionOrder(-100)]
    public sealed class RadarNavigation : MonoBehaviour
    {
        public RadarSettings settings;
        public BoatControls controls;
        public Rigidbody boatRigidbody;
        [Tooltip("HDRP water surfaces to move around the stationary boat. Empty: find surfaces in this scene.")]
        [SerializeField] private WaterSurface[] waterSurfaces = new WaterSurface[0];
        private float initialHeading;
        public Vector2 Position { get; private set; }
        public float Heading { get; private set; }
        public float CurrentSpeed { get; private set; }

        private void Start()
        {
            Heading = boatRigidbody != null ? boatRigidbody.rotation.eulerAngles.y : transform.eulerAngles.y;
            initialHeading = Heading;
            if (waterSurfaces == null || waterSurfaces.Length == 0)
                waterSurfaces = System.Array.FindAll(
                    FindObjectsByType<WaterSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                    surface => surface.gameObject.scene == gameObject.scene);
        }

        private void FixedUpdate() => Simulate(Time.fixedDeltaTime);

        public void Simulate(float dt)
        {
            if (controls == null || settings == null || dt <= 0f) return;
            float target = controls.Throttle01 * Mathf.Max(0f, settings.maxSpeed);
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, target,
                Mathf.Max(0.01f, target > CurrentSpeed ? settings.acceleration : settings.deceleration) * dt);
            float yaw = controls.ThrottleStep > 0 && CurrentSpeed > 0f
                ? controls.Steering * settings.turnSpeed * dt
                : 0f;
            Heading = Mathf.Repeat(Heading + yaw, 360f);
            Vector2 travel = Direction(Heading) * CurrentSpeed * dt;
            Position += travel;
            if (settings.waterTravelEnabled || settings.rotateWaterWithSteering)
            {
                Vector3 pivot = boatRigidbody != null ? boatRigidbody.position : transform.position;
                // Convert virtual navigation into the stationary hull's frame, then move
                // the HDRP simulation opposite to the boat's travel and steering.
                Vector3 displacement = new Vector3(travel.x, 0f, travel.y)
                    * Mathf.Max(0f, settings.waterSpeedMultiplier);
                if (settings.rotateWaterWithSteering)
                    displacement = Quaternion.Euler(0f, initialHeading - Heading, 0f) * displacement;
                if (waterSurfaces == null) return;
                foreach (WaterSurface surface in waterSurfaces)
                {
                    if (surface == null || !surface.isActiveAndEnabled || surface.gameObject.scene != gameObject.scene) continue;
                    if (settings.rotateWaterWithSteering)
                        surface.transform.RotateAround(pivot, Vector3.up, -yaw);
                    if (settings.waterTravelEnabled)
                        surface.transform.position -= displacement;
                }
            }
        }

        public static Vector2 Direction(float heading)
        {
            float radians = heading * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        public Vector2 WorldToRadar(Vector2 worldPosition)
        {
            Vector2 delta = worldPosition - Position;
            float radians = Heading * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            return new Vector2(cos * delta.x - sin * delta.y, sin * delta.x + cos * delta.y);
        }
    }
}
