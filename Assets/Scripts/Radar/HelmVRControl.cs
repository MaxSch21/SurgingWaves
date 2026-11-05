using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgingWaves.Radar
{
    /// <summary>
    /// Drives the existing helm values from a grabbed controller's movement around a fixed pivot.
    /// HelmMouseControl remains responsible for displaying those values on its Visual child.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HelmMouseControl), typeof(XRGrabInteractable))]
    [DefaultExecutionOrder(-10)] // Set values before HelmMouseControl refreshes its visual in LateUpdate.
    public sealed class HelmVRControl : MonoBehaviour
    {
        [SerializeField, Range(0f, 0.45f)]
        [Tooltip("Extra fraction of the angle between throttle notches needed to switch back. Prevents jitter at a notch boundary.")]
        private float leverNotchHysteresis = 0.1f;

        [SerializeField, Min(0.001f)]
        [Tooltip("Ignore controller positions this close to the rotation axis, in metres, to avoid unstable angles.")]
        private float minimumGrabRadius = 0.025f;

        private HelmMouseControl helm;
        private XRGrabInteractable grab;
        private Transform hand;
        private Transform mountParent;
        private Vector3 mountPosition;
        private Quaternion mountRotation;
        private Vector3 mountScale;
        private Quaternion restRotation;
        private Vector3 previousDirection;
        private bool hasPreviousDirection;
        private bool held;
        private float leverAngle;

        private void Awake()
        {
            helm = GetComponent<HelmMouseControl>();
            grab = GetComponent<XRGrabInteractable>();
            mountParent = transform.parent;
            mountPosition = transform.localPosition;
            mountRotation = transform.localRotation;
            mountScale = transform.localScale;

            if (helm.visual == null || helm.visual.parent != transform || helm.controls == null || helm.settings == null)
            {
                Debug.LogError("HelmVRControl needs HelmMouseControl's Settings, Controls and a direct Visual child assigned.", this);
                enabled = false;
                return;
            }

            restRotation = helm.visual.localRotation;
        }

        private void OnEnable()
        {
            grab.selectEntered.AddListener(OnGrab);
            grab.selectExited.AddListener(OnRelease);
        }

        private void OnDisable()
        {
            grab.selectEntered.RemoveListener(OnGrab);
            grab.selectExited.RemoveListener(OnRelease);
            EndGrab();
        }

        private void OnGrab(SelectEnterEventArgs args)
        {
            // XRGrabInteractable unparents even when all tracking is off. Keep the helm mounted
            // so it also follows the boat if the station is moved while being held.
            RestoreMount();
            if (!helm.isActiveAndEnabled || helm.IsHeld) return;

            // Use the tracked interactor itself. Near-Far's attach anchor can move independently
            // during distant grabs and must not be used as the physical hand position.
            hand = args.interactorObject.transform;
            helm.BeginGrab();
            held = true;
            leverAngle = (helm.controls.ThrottleStep - 1f) * helm.settings.leverMaxAngle;
            hasPreviousDirection = TryGetDirection(out previousDirection);
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            EndGrab();
            RestoreMount();
        }

        private void EndGrab()
        {
            if (held) helm.EndGrab();
            held = false;
            hand = null;
            hasPreviousDirection = false;
        }

        private void RestoreMount()
        {
            transform.SetParent(mountParent, false);
            transform.SetLocalPositionAndRotation(mountPosition, mountRotation);
            transform.localScale = mountScale;
        }

        private void LateUpdate()
        {
            if (!held) return;
            if (hand == null || !grab.isSelected || !helm.isActiveAndEnabled)
            {
                EndGrab();
                return;
            }

            if (!TryGetDirection(out Vector3 direction))
            {
                // Rebase when leaving the axis dead zone instead of applying a sudden angle jump.
                hasPreviousDirection = false;
                return;
            }

            if (hasPreviousDirection)
            {
                bool wheel = helm.controlType == HelmMouseControl.ControlType.SteeringWheel;
                float delta = Vector3.SignedAngle(previousDirection, direction, wheel ? Vector3.forward : Vector3.right);
                if (wheel)
                {
                    // The existing wheel visual uses -Steering around its local Z axis.
                    helm.controls.SetSteering(helm.controls.Steering - delta / Mathf.Max(1f, helm.settings.wheelMaxAngle));
                }
                else
                {
                    float maxAngle = Mathf.Max(1f, helm.settings.leverMaxAngle);
                    leverAngle = Mathf.Clamp(leverAngle + delta, -maxAngle, maxAngle);
                    helm.controls.SetThrottleStep(GetThrottleStep(helm.controls.ThrottleStep, leverAngle, maxAngle, leverNotchHysteresis));
                }
            }

            // Incremental changes discard travel past the end stops so reversing responds immediately.
            previousDirection = direction;
            hasPreviousDirection = true;
        }

        private bool TryGetDirection(out Vector3 direction)
        {
            Vector3 axis = helm.controlType == HelmMouseControl.ControlType.SteeringWheel ? Vector3.forward : Vector3.right;
            Vector3 worldAxis = transform.TransformDirection(restRotation * axis);
            Vector3 offset = Vector3.ProjectOnPlane(hand.position - helm.visual.position, worldAxis);
            direction = Quaternion.Inverse(restRotation) * transform.InverseTransformDirection(offset);
            return offset.sqrMagnitude >= minimumGrabRadius * minimumGrabRadius;
        }

        private static int GetThrottleStep(int step, float angle, float maxAngle, float hysteresis)
        {
            while (step < 2 && angle > (step - 0.5f + hysteresis) * maxAngle) step++;
            while (step > 0 && angle < (step - 1.5f - hysteresis) * maxAngle) step--;
            return step;
        }
    }
}
