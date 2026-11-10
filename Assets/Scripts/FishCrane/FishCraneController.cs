using UnityEngine;

namespace SurgingWaves.FishCrane
{
    public sealed class FishCraneController : MonoBehaviour
    {
        private enum Phase { LowerOutside, Waiting, RaiseOutside, LowerInside, RaiseInside, ReturnMiddle }

        [Header("Parts")]
        [SerializeField] private Transform net;
        [SerializeField] private Collider netCollider;
        [SerializeField] private Transform crank;

        [Header("Swing angles relative to the initial rotation")]
        [SerializeField] private float middleAngle;
        [SerializeField] private float outsideAngle = 90f;
        [SerializeField] private float insideAngle = -90f;
        [SerializeField, Min(0f)] private float snapTolerance = 4f;
        [SerializeField, Min(0f)] private float snapSpeed = 35f;

        [Header("Net travel in local units")]
        [SerializeField, Min(0.01f)] private float outsideDepth = 5f;
        [SerializeField, Min(0.01f)] private float insideDepth = 3f;
        [SerializeField, Min(0f)] private float raisedTolerance = 0.03f;
        [SerializeField, Min(0f)] private float waitDuration = 5f;
        [SerializeField, Min(0.001f)] private float travelPerRevolution = 0.5f;
        [SerializeField] private Vector3 crankLocalAxis = Vector3.right;

        [Header("Boat floor")]
        [SerializeField, Tooltip("Optional specific deck collider. Otherwise detect solid surfaces below the net.")]
        private Collider boatFloor;
        [SerializeField] private LayerMask floorLayers = ~0;
        [SerializeField, Min(0f), Tooltip("World-space clearance between the net and deck.")]
        private float floorClearance = 0.01f;

        private Quaternion initialRotation;
        private Quaternion initialCrankRotation;
        private Vector3 initialNetPosition;
        private Vector3 netLocalDown;
        private Phase phase;
        private float angle;
        private float netTravel;
        private float crankAngle;
        private float waitUntil;
        private bool turnHeld;
        private bool crankHeld;

        public bool CanTurn => isActiveAndEnabled && net != null && !crankHeld
            && netTravel <= raisedTolerance && phase != Phase.Waiting;
        public bool CanCrank => isActiveAndEnabled && net != null && crank != null && !turnHeld
            && phase != Phase.Waiting && phase != Phase.ReturnMiddle && AtWorkingAngle;
        private bool Inboard => phase == Phase.LowerInside || phase == Phase.RaiseInside || phase == Phase.ReturnMiddle;
        private float WorkingAngle => Inboard ? insideAngle : outsideAngle;
        private bool AtWorkingAngle => Mathf.Abs(angle - WorkingAngle) <= snapTolerance;

        private void Awake()
        {
            initialRotation = transform.localRotation;
            if (net != null)
            {
                initialNetPosition = net.localPosition;
                netLocalDown = net.localRotation * Vector3.down;
            }
            if (crank != null) initialCrankRotation = crank.localRotation;
            SetAngle(middleAngle);
        }

        private void Update()
        {
            if (phase == Phase.Waiting && Time.time >= waitUntil)
                phase = Phase.RaiseOutside;

            if (!turnHeld && !crankHeld && CanTurn && snapSpeed > 0f)
            {
                float target = middleAngle;
                if (Mathf.Abs(angle - outsideAngle) < Mathf.Abs(angle - target)) target = outsideAngle;
                if (Inboard && Mathf.Abs(angle - insideAngle) < Mathf.Abs(angle - target)) target = insideAngle;
                if (Mathf.Abs(angle - target) <= snapTolerance)
                    SetAngle(Mathf.MoveTowards(angle, target, snapSpeed * Time.deltaTime));
            }

            if (phase == Phase.ReturnMiddle && Mathf.Abs(angle - middleAngle) <= 0.0001f)
                phase = Phase.LowerOutside;
        }

        public bool BeginTurn()
        {
            if (!CanTurn || turnHeld) return false;
            turnHeld = true;
            return true;
        }

        public bool BeginCrank()
        {
            if (!CanCrank || crankHeld) return false;
            crankHeld = true;
            return true;
        }

        public void EndTurn() => turnHeld = false;
        public void EndCrank() => crankHeld = false;

        public void Turn(float degrees)
        {
            if (!turnHeld || !CanTurn) return;
            float lower = Mathf.Min(middleAngle, outsideAngle);
            float upper = Mathf.Max(middleAngle, outsideAngle);
            if (phase == Phase.ReturnMiddle)
            {
                lower = Mathf.Min(middleAngle, insideAngle);
                upper = Mathf.Max(middleAngle, insideAngle);
            }
            else if (Inboard)
            {
                lower = Mathf.Min(lower, insideAngle);
                upper = Mathf.Max(upper, insideAngle);
            }
            SetAngle(Mathf.Clamp(angle + degrees, lower, upper));
        }

        public void RotateCrank(float degrees)
        {
            if (!crankHeld || !CanCrank) return;
            // Positive crank rotation raises the net; negative rotation lowers it.
            bool raising = phase == Phase.RaiseOutside || phase == Phase.RaiseInside;
            if ((raising && degrees <= 0f) || (!raising && degrees >= 0f)) return;

            float previous = netTravel;
            float limit = Inboard ? insideDepth : outsideDepth;
            float requested = Mathf.Clamp(netTravel - degrees / 360f * travelPerRevolution, 0f, limit);
            bool deckReached = false;
            if (Inboard && requested > netTravel)
            {
                float movement = LimitAtFloor(requested - netTravel, out deckReached);
                requested = netTravel + movement;
            }
            SetNetTravel(requested);
            crankAngle = Mathf.Repeat(crankAngle + (previous - netTravel) / travelPerRevolution * 360f, 360f);
            Vector3 axis = crankLocalAxis.sqrMagnitude > 0.0001f ? crankLocalAxis.normalized : Vector3.right;
            crank.localRotation = initialCrankRotation * Quaternion.AngleAxis(crankAngle, axis);

            if (phase == Phase.LowerOutside && netTravel >= outsideDepth - 0.0001f)
            {
                phase = Phase.Waiting;
                waitUntil = Time.time + waitDuration;
            }
            else if (phase == Phase.RaiseOutside && netTravel <= raisedTolerance)
            {
                SetNetTravel(0f);
                phase = Phase.LowerInside;
            }
            else if (phase == Phase.LowerInside && (deckReached || netTravel >= insideDepth - 0.0001f))
                phase = Phase.RaiseInside;
            else if (phase == Phase.RaiseInside && netTravel <= raisedTolerance)
            {
                SetNetTravel(0f);
                phase = Phase.ReturnMiddle;
            }
        }

        private float LimitAtFloor(float localMovement, out bool reached)
        {
            reached = false;
            if (netCollider == null) return 0f;
            Vector3 worldStep = net.parent != null ? net.parent.TransformVector(netLocalDown) : netLocalDown;
            float unitsToWorld = worldStep.magnitude;
            if (unitsToWorld < 0.0001f) return 0f;
            float worldMovement = localMovement * unitsToWorld;
            Physics.SyncTransforms();
            Bounds bounds = netCollider.bounds;
            RaycastHit[] hits = Physics.BoxCastAll(bounds.center, bounds.extents, worldStep / unitsToWorld,
                Quaternion.identity, worldMovement + floorClearance, floorLayers, QueryTriggerInteraction.Ignore);
            float allowed = worldMovement;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (boatFloor != null && hit.collider != boatFloor) continue;
                float distance = Mathf.Max(0f, hit.distance - floorClearance);
                if (distance > allowed) continue;
                allowed = distance;
                reached = true;
            }
            return allowed / unitsToWorld;
        }

        private void SetAngle(float value)
        {
            angle = value;
            transform.localRotation = initialRotation * Quaternion.AngleAxis(angle, Vector3.up);
        }

        private void SetNetTravel(float value)
        {
            netTravel = value;
            net.localPosition = initialNetPosition + netLocalDown * netTravel;
        }

        private void OnDisable()
        {
            turnHeld = false;
            crankHeld = false;
        }

        public string TurnHint => phase == Phase.ReturnMiddle ? "Hold E + drag left/right to return the crane to the middle"
            : CanTurn ? "Hold E + drag left/right to swing the crane"
            : "Raise the net before swinging the crane";

        public string CrankHint
        {
            get
            {
                if (phase == Phase.ReturnMiddle) return "Return the crane to the middle";
                if (phase == Phase.Waiting) return $"Wait for the net: {Mathf.Max(0f, waitUntil - Time.time):0.0} s";
                if (!AtWorkingAngle) return Inboard ? "Swing the crane inboard to use the crank"
                    : "Swing the crane outside to use the crank";
                if (phase == Phase.RaiseOutside || phase == Phase.RaiseInside)
                    return "Hold E + drag mouse up to crank the net up";
                return "Hold E + drag mouse down to crank the net down";
            }
        }
    }
}
