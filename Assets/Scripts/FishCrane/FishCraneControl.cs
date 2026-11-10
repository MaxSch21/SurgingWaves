using UnityEngine;

namespace SurgingWaves.FishCrane
{
    public sealed class FishCraneControl : MonoBehaviour
    {
        public enum ControlType { TurnHandle, Crank }

        [SerializeField] private FishCraneController crane;
        [SerializeField] private ControlType controlType;
        [SerializeField, Min(0.1f)] private float grabDistance = 3f;
        [SerializeField] private float turnDegreesPerPixel = 0.3f;
        [SerializeField] private float crankDegreesPerPixel = 2f;

        private Vector3 grabLocalPoint;
        private bool held;

        public bool IsHeld => held;
        public float GrabDistance => grabDistance;
        public Vector3 GrabPoint => transform.TransformPoint(grabLocalPoint);
        public bool CanInteract => isActiveAndEnabled && crane != null
            && (controlType == ControlType.TurnHandle ? crane.CanTurn : crane.CanCrank);
        public string Hint => crane == null ? string.Empty
            : controlType == ControlType.TurnHandle ? crane.TurnHint : crane.CrankHint;

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
            if (!held) return;
            held = false;
            if (crane == null) return;
            if (controlType == ControlType.TurnHandle) crane.EndTurn();
            else crane.EndCrank();
        }

        private void OnDisable() => EndGrab();
    }
}
