using UnityEngine;
using UnityEngine.InputSystem;

namespace SurgingWaves.Radar
{
    [RequireComponent(typeof(DebugFpsController))]
    public sealed class FpsHelmInteractor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float searchDistance = 5f;
        private HelmMouseControl grabbed;
        private HelmMouseControl target;
        public HelmMouseControl Grabbed => grabbed;

        // Called by FPS controller before mouse look, sharing its focus/menu gate.
        public bool ProcessInput(Camera view, bool acceptInput)
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (!acceptInput || view == null || keyboard == null || mouse == null)
            { Release(); target = null; return false; }
            if (grabbed != null)
            {
                if (!keyboard.eKey.isPressed || !grabbed.isActiveAndEnabled ||
                    Vector3.Distance(view.transform.position, grabbed.transform.position) > grabbed.settings.grabDistance)
                { Release(); return false; }
                grabbed.ApplyMouseDelta(mouse.delta.ReadValue());
                return true;
            }

            target = null;
            if (Physics.Raycast(view.transform.position, view.transform.forward, out RaycastHit hit,
                searchDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                HelmMouseControl control = hit.collider.GetComponentInParent<HelmMouseControl>();
                if (control != null && control.isActiveAndEnabled && control.settings != null &&
                    hit.distance <= control.settings.grabDistance) target = control;
            }
            if (target != null && keyboard.eKey.wasPressedThisFrame)
            { grabbed = target; grabbed.BeginGrab(); return true; }
            return false;
        }

        private void Release() { if (grabbed != null) grabbed.EndGrab(); grabbed = null; }
        private void OnDisable() { Release(); target = null; }
        private void OnApplicationFocus(bool focused) { if (!focused) { Release(); target = null; } }

        private void OnGUI()
        {
            DebugFpsController fps = GetComponent<DebugFpsController>();
            if (fps.InputBlocked || Cursor.lockState != CursorLockMode.Locked) return;
            GUI.Label(new Rect(Screen.width * 0.5f - 4f, Screen.height * 0.5f - 10f, 16f, 24f), "+");
            HelmMouseControl control = grabbed != null ? grabbed : target;
            if (control == null) return;
            string text = grabbed == null ? "E gedrueckt halten: " + control.Label
                : control.controlType == HelmMouseControl.ControlType.SteeringWheel
                    ? "E halten + Maus links/rechts | Loslassen: E"
                    : "E halten + Maus hoch/runter | Rastung: " + control.controls.ThrottleStep
                        + " (0 / 1 / 2) | Weiterziehen ueberwindet Widerstand";
            GUI.Box(new Rect((Screen.width - 650f) * 0.5f, Screen.height - 82f, 650f, 42f), text);
        }
    }
}
