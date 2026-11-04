using UnityEngine;

namespace SurgingWaves.Radar
{
    public sealed class BoatControls : MonoBehaviour
    {
        [SerializeField, Range(0, 2)] private int throttleStep;
        [SerializeField, Range(-1f, 1f)] private float steering;
        public int ThrottleStep => Mathf.Clamp(throttleStep, 0, 2);
        public float Throttle01 => ThrottleStep * 0.5f;
        public float Steering => Mathf.Clamp(steering, -1f, 1f);
        public void SetThrottleStep(int step) => throttleStep = Mathf.Clamp(step, 0, 2);
        public void SetSteering(float value) => steering = Mathf.Clamp(value, -1f, 1f);
    }
}
