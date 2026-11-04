using UnityEngine;
using UnityEngine.Serialization;

namespace SurgingWaves.Radar
{
    [CreateAssetMenu(menuName = "Surging Waves/Radar Settings")]
    public sealed class RadarSettings : ScriptableObject
    {
        [Header("Navigation (virtual metres / seconds)")]
        [Min(0f)] public float maxSpeed = 8f;
        [Min(0.01f)] public float acceleration = 3f;
        [Min(0.01f)] public float deceleration = 5f;
        [Range(0f, 20f)] public float turnSpeed = 4f;
        [FormerlySerializedAs("rotateRealBoat")]
        [Tooltip("Turn the sea around the stationary boat when steering; keep the hull's heading fixed.")]
        public bool rotateWaterWithSteering = true;

        [Header("Water Travel")]
        [FormerlySerializedAs("surfaceFoamTravelEnabled")]
        [Tooltip("Move the entire wave field, surface details and foam past the stationary boat.")]
        public bool waterTravelEnabled = true;
        [FormerlySerializedAs("surfaceFoamSpeedMultiplier")]
        [Range(0f, 3f), Tooltip("Water travel relative to boat speed. 0: no additional motion; 1: matches boat speed.")]
        public float waterSpeedMultiplier = 1f;

        [Header("Contacts (one shared range)")]
        [Min(10f)] public float range = 100f;
        [Range(1, 20)] public int fishCount = 4;
        [Range(0.1f, 0.95f)] public float spawnNear = 0.5f;
        [Range(0.1f, 0.95f)] public float spawnFar = 0.85f;
        [Range(0f, 60f)] public float spawnHalfAngle = 55f;
        [Min(0.1f)] public float reachDistance = 7f;

        [Header("Desktop interaction")]
        [Min(0.1f)] public float grabDistance = 2.5f;
        [Min(1f)] public float wheelMaxAngle = 120f;
        [Min(0.01f)] public float wheelDegreesPerPixel = 0.3f;
        [Range(5f, 70f)] public float leverMaxAngle = 35f;
        [Min(10f), Tooltip("Mouse pixels needed to leave each notch. Movement accumulates while E is held.")]
        public float leverNotchResistance = 65f;
        [Range(0.05f, 0.4f)] public float leverNotchTravel = 0.18f;

        [Header("Display")]
        public Color background = new Color(0.025f, 0.1f, 0.115f, 1f);
        public Color grid = new Color(0.15f, 0.4f, 0.33f, 1f);
        public Color signal = new Color(0.43f, 0.96f, 0.71f, 1f);
        public Color monster = new Color(1f, 0.65f, 0.3f, 1f);
        [Min(0f)] public float sweepDegreesPerSecond = 45f;
    }
}
