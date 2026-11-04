using UnityEngine;

namespace SurgingWaves.Radar
{
    public sealed class HelmMouseControl : MonoBehaviour
    {
        public enum ControlType { SteeringWheel, ThrottleLever }
        public ControlType controlType;
        public RadarSettings settings;
        public BoatControls controls;
        public Transform visual;
        private Quaternion restRotation;
        private float notchPressure;
        private bool held;
        public bool IsHeld => held;
        public float NotchPressure => notchPressure;
        public string Label => controlType == ControlType.SteeringWheel ? "Steuerrad" : "Gashebel";

        private void Awake() { if (visual != null) restRotation = visual.localRotation; }
        private void LateUpdate() => RefreshVisual();
        private void OnDisable() => EndGrab();
        public void BeginGrab() { notchPressure = 0f; held = true; }
        public void EndGrab() { held = false; notchPressure = 0f; RefreshVisual(); }

        public void ApplyMouseDelta(Vector2 delta)
        {
            if (!held || controls == null || settings == null) return;
            if (controlType == ControlType.SteeringWheel)
            {
                controls.SetSteering(controls.Steering + delta.x * settings.wheelDegreesPerPixel
                    / Mathf.Max(1f, settings.wheelMaxAngle));
            }
            else
            {
                float resistance = Mathf.Max(10f, settings.leverNotchResistance);
                // Opposite movement first removes existing pressure. At the end stops
                // discard excess movement so reversing responds immediately.
                notchPressure += delta.y;
                while (notchPressure >= resistance && controls.ThrottleStep < 2)
                { controls.SetThrottleStep(controls.ThrottleStep + 1); notchPressure -= resistance; }
                while (notchPressure <= -resistance && controls.ThrottleStep > 0)
                { controls.SetThrottleStep(controls.ThrottleStep - 1); notchPressure += resistance; }
                if (controls.ThrottleStep == 2) notchPressure = Mathf.Min(0f, notchPressure);
                if (controls.ThrottleStep == 0) notchPressure = Mathf.Max(0f, notchPressure);
            }
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (visual == null || controls == null || settings == null) return;
            if (controlType == ControlType.SteeringWheel)
                visual.localRotation = restRotation * Quaternion.AngleAxis(
                    -controls.Steering * settings.wheelMaxAngle, Vector3.forward);
            else
            {
                float fraction = notchPressure / Mathf.Max(10f, settings.leverNotchResistance);
                float offset = Mathf.Abs(fraction) < settings.leverNotchTravel ? 0f : fraction * 0.4f;
                visual.localRotation = restRotation * Quaternion.AngleAxis(
                    (controls.ThrottleStep - 1f + offset) * settings.leverMaxAngle, Vector3.right);
            }
        }
    }
}
