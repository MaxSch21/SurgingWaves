using UnityEngine;

namespace SurgingWaves.Radio
{
    public sealed class RadioMouseControl : MonoBehaviour
    {
        public enum ControlType { PowerKnob, StationButton }

        [SerializeField] private ControlType controlType;
        [SerializeField] private RadioController radio;
        [SerializeField] private Transform visual;
        [SerializeField, Min(0.1f)] private float grabDistance = 2f;

        [Header("Drehknopf")]
        [SerializeField, Min(1f), Tooltip("Horizontale Mauspixel für die vollständige Drehung zwischen Aus und Ein.")]
        private float notchResistance = 60f;
        [SerializeField] private float onAngle = -90f;
        [SerializeField, Min(1f), Tooltip("Rücklauf zur bisherigen Rastposition beim Loslassen, in Grad pro Sekunde.")]
        private float returnSpeed = 360f;

        [Header("Sendertaste")]
        [SerializeField] private Vector3 pressOffset = new Vector3(0f, 0f, 0.008f);

        private Vector3 restPosition;
        private Quaternion restRotation;
        private float knobPosition;
        private bool held;

        public float GrabDistance => grabDistance;
        public bool IsHeld => held;
        public bool IsPowerKnob => controlType == ControlType.PowerKnob;
        public bool CanInteract => isActiveAndEnabled && radio != null && radio.isActiveAndEnabled
            && (IsPowerKnob || radio.IsOn);
        public string Hint => !IsPowerKnob ? "E druecken: Naechster Radiosender"
            : held ? "E halten + Maus links/rechts | Radio: " + (radio.IsOn ? "Ein" : "Aus")
            : "E gedrueckt halten: Radio Ein/Aus";

        private void Awake()
        {
            knobPosition = radio != null && radio.IsOn ? 1f : 0f;
            if (visual == null) return;
            restPosition = visual.localPosition;
            restRotation = visual.localRotation;
        }

        private void LateUpdate()
        {
            if (IsPowerKnob && radio != null && (!held || !CanInteract))
                knobPosition = Mathf.MoveTowards(knobPosition, radio.IsOn ? 1f : 0f,
                    returnSpeed / Mathf.Max(1f, Mathf.Abs(onAngle)) * Time.deltaTime);
            RefreshVisual();
        }
        private void OnDisable() => EndGrab();

        public void BeginGrab()
        {
            if (held || !CanInteract) return;
            held = true;
            if (!IsPowerKnob) radio.NextStation();
            RefreshVisual();
        }

        public void EndGrab()
        {
            held = false;
            RefreshVisual();
        }

        public void ApplyMouseDelta(Vector2 delta)
        {
            if (!held || !CanInteract || !IsPowerKnob) return;
            knobPosition = Mathf.Clamp01(knobPosition + delta.x / Mathf.Max(1f, notchResistance));
            if (knobPosition >= 1f) radio.SetPower(true);
            else if (knobPosition <= 0f) radio.SetPower(false);
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (visual == null || radio == null) return;
            if (IsPowerKnob)
                visual.localRotation = restRotation * Quaternion.AngleAxis(knobPosition * onAngle, Vector3.forward);
            else
                visual.localPosition = restPosition + (held && CanInteract ? pressOffset : Vector3.zero);
        }
    }
}
