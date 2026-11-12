using UnityEngine;
using UnityEngine.InputSystem;
using SurgingWaves.Radio;
using SurgingWaves.Interaction;
using SurgingWaves.FishCrane;

namespace SurgingWaves.Radar
{
    [RequireComponent(typeof(DebugFpsController))]
    public sealed class FpsHelmInteractor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float searchDistance = 5f;
        private HelmMouseControl grabbed;
        private HelmMouseControl target;
        private RadioMouseControl grabbedRadio;
        private RadioMouseControl radioTarget;
        private HingedDoor grabbedDoor;
        private HingedDoor doorTarget;
        private Vector3 doorTargetPoint;
        private string doorHint;
        private FishCraneControl grabbedCrane;
        private FishCraneControl craneTarget;
        private Vector3 craneTargetPoint;
        public HelmMouseControl Grabbed => grabbed;

        // Called by FPS controller before mouse look, sharing its focus/menu gate.
        public bool ProcessInput(Camera view, bool acceptInput)
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (!acceptInput || view == null || keyboard == null || mouse == null)
            { Release(); target = null; radioTarget = null; doorTarget = null; doorHint = null; craneTarget = null; return false; }
            if (grabbed != null)
            {
                if (!keyboard.eKey.isPressed || !grabbed.isActiveAndEnabled ||
                    Vector3.Distance(view.transform.position, grabbed.transform.position) > grabbed.settings.grabDistance)
                { Release(); return false; }
                grabbed.ApplyMouseDelta(mouse.delta.ReadValue());
                return true;
            }

            if (grabbedRadio != null)
            {
                if (!keyboard.eKey.isPressed || !grabbedRadio.CanInteract ||
                    Vector3.Distance(view.transform.position, grabbedRadio.transform.position) > grabbedRadio.GrabDistance)
                { Release(); return false; }
                grabbedRadio.ApplyMouseDelta(mouse.delta.ReadValue());
                return true;
            }

            if (grabbedDoor != null)
            {
                if (!keyboard.eKey.isPressed || !grabbedDoor.isActiveAndEnabled || !grabbedDoor.IsHeld ||
                    Vector3.Distance(view.transform.position, grabbedDoor.GrabPoint) > grabbedDoor.GrabDistance)
                { Release(); return false; }
                grabbedDoor.ApplyMouseDelta(mouse.delta.ReadValue());
                return true;
            }

            if (grabbedCrane != null)
            {
                if (!keyboard.eKey.isPressed || !grabbedCrane.IsHeld || !grabbedCrane.CanInteract ||
                    Vector3.Distance(view.transform.position, grabbedCrane.GrabPoint) > grabbedCrane.GrabDistance)
                { Release(); return false; }
                grabbedCrane.ApplyMouseDelta(mouse.delta.ReadValue());
                return true;
            }

            target = null;
            radioTarget = null;
            doorTarget = null;
            doorHint = null;
            craneTarget = null;
            if (Physics.Raycast(view.transform.position, view.transform.forward, out RaycastHit hit,
                searchDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                HelmMouseControl control = hit.collider.GetComponentInParent<HelmMouseControl>();
                if (control != null && control.isActiveAndEnabled && !control.IsHeld && control.settings != null &&
                    hit.distance <= control.settings.grabDistance) target = control;
                RadioMouseControl radioControl = hit.collider.GetComponentInParent<RadioMouseControl>();
                if (target == null && radioControl != null && radioControl.CanInteract && !radioControl.IsHeld &&
                    hit.distance <= radioControl.GrabDistance) radioTarget = radioControl;
                HingedDoor door = hit.collider.GetComponentInParent<HingedDoor>();
                if (target == null && radioTarget == null && door != null && door.isActiveAndEnabled && !door.IsHeld &&
                    hit.distance <= door.GrabDistance)
                {
                    if (!door.CanGrabAt(hit.point))
                        doorHint = "Aim at the half opposite the hinge";
                    else
                    { doorTarget = door; doorTargetPoint = hit.point; }
                }
                FishCraneControl craneControl = hit.collider.GetComponentInParent<FishCraneControl>();
                if (target == null && radioTarget == null && doorTarget == null && craneControl != null &&
                    craneControl.isActiveAndEnabled && !craneControl.IsHeld && hit.distance <= craneControl.GrabDistance)
                { craneTarget = craneControl; craneTargetPoint = hit.point; }
            }
            if (target != null && keyboard.eKey.wasPressedThisFrame)
            { grabbed = target; grabbed.BeginGrab(); return true; }
            if (radioTarget != null && keyboard.eKey.wasPressedThisFrame)
            { grabbedRadio = radioTarget; grabbedRadio.BeginGrab(); return true; }
            if (doorTarget != null && keyboard.eKey.wasPressedThisFrame && doorTarget.BeginGrab(doorTargetPoint))
            { grabbedDoor = doorTarget; return true; }
            if (craneTarget != null && keyboard.eKey.wasPressedThisFrame && craneTarget.BeginGrab(craneTargetPoint))
            { grabbedCrane = craneTarget; return true; }
            return false;
        }

        private void Release()
        {
            if (grabbed != null) grabbed.EndGrab();
            grabbed = null;
            if (grabbedRadio != null) grabbedRadio.EndGrab();
            grabbedRadio = null;
            if (grabbedDoor != null) grabbedDoor.EndGrab();
            grabbedDoor = null;
            if (grabbedCrane != null) grabbedCrane.EndGrab();
            grabbedCrane = null;
        }
        private void OnDisable() { Release(); target = null; radioTarget = null; doorTarget = null; doorHint = null; craneTarget = null; }
        private void OnApplicationFocus(bool focused) { if (!focused) { Release(); target = null; radioTarget = null; doorTarget = null; doorHint = null; craneTarget = null; } }

        private void OnGUI()
        {
            DebugFpsController fps = GetComponent<DebugFpsController>();
            if (fps.InputBlocked || Cursor.lockState != CursorLockMode.Locked) return;
            GUI.Label(new Rect(Screen.width * 0.5f - 4f, Screen.height * 0.5f - 10f, 16f, 24f), "+");
            FishCraneControl craneControl = grabbedCrane != null ? grabbedCrane : craneTarget;
            if (craneControl != null)
            {
                GUI.Box(new Rect((Screen.width - 650f) * 0.5f, Screen.height - 82f, 650f, 42f), craneControl.Hint);
                return;
            }
            HingedDoor door = grabbedDoor != null ? grabbedDoor : doorTarget;
            if (door != null)
            {
                string hint = grabbedDoor != null
                    ? $"Hold E + drag mouse left/right | {door.Label}: {door.CurrentAngle:0}° | Release E to let go"
                    : "Hold E to move the " + door.Label.ToLowerInvariant();
                GUI.Box(new Rect((Screen.width - 650f) * 0.5f, Screen.height - 82f, 650f, 42f), hint);
                return;
            }
            if (doorHint != null)
            {
                GUI.Box(new Rect((Screen.width - 650f) * 0.5f, Screen.height - 82f, 650f, 42f), doorHint);
                return;
            }
            RadioMouseControl radioControl = grabbedRadio != null ? grabbedRadio : radioTarget;
            if (radioControl != null && radioControl.CanInteract)
            {
                GUI.Box(new Rect((Screen.width - 650f) * 0.5f, Screen.height - 82f, 650f, 42f), radioControl.Hint);
                return;
            }
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
